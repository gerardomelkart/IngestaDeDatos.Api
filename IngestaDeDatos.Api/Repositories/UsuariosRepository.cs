using IngestaDeDatos.Api.Configuration;
using Microsoft.Extensions.Options;
using IngestaDeDatos.Api.Validators;
using Dapper;
using IngestaDeDatos.Api.Data;
using IngestaDeDatos.Api.Models;
using System.Security.Claims;
using System.Data;
using Microsoft.Data.SqlClient;

namespace IngestaDeDatos.Api.Repositories;

public sealed class UsuariosRepository(DbFactory factory, ICatalogosRepository catalogos, IOptions<ReglasDdcpOptions> options) : IUsuariosRepository
{
    private ReglasDdcpOptions Reglas => options.Value;
    private const string Columnas = "u.ID_USUARIO IdUsuario, u.USUARIO Usuario, u.NOMBRE Nombre, u.PASSWORD_HASH PasswordHash, r.CLAVE Rol, r.NOMBRE NombreRol, u.ES_NACIONAL EsNacional, u.ID_ENTIDAD IdEntidad, u.HABILITADO Habilitado, r.PUEDE_CARGAR PuedeCargar, CASE WHEN r.HABILITADO = 1 THEN r.PUEDE_ADMINISTRAR_USUARIOS ELSE 0 END PuedeAdministrarUsuarios, r.NACIONAL_OBLIGATORIO NacionalObligatorio";
    private const string Desde = "FROM dbo.ING_USUARIO u JOIN dbo.CAT_ROL r ON r.CLAVE = u.ROL";
    private async Task<RolCatalogo> ObtenerRol(string clave, Microsoft.Data.SqlClient.SqlConnection db, System.Data.Common.DbTransaction tx)
    {
        return await db.QuerySingleOrDefaultAsync<RolCatalogo>($"SELECT {CatalogosRepository.RolColumnas} FROM dbo.CAT_ROL WITH (HOLDLOCK) WHERE CLAVE = @clave AND HABILITADO = 1;", new { clave }, tx)
            ?? throw new ArgumentException("Rol inexistente o deshabilitado.");
    }

    public async Task<UsuarioInfo?> Buscar(string usuario)
    {
        await using var db = factory.Auth();
        return await db.QuerySingleOrDefaultAsync<UsuarioInfo>($"SELECT {Columnas} {Desde} WHERE u.USUARIO = @usuario AND u.HABILITADO = 1 AND r.HABILITADO = 1 AND (r.NACIONAL_OBLIGATORIO = 0 OR u.ES_NACIONAL = 1);", new { usuario });
    }
    public async Task<UsuarioInfo> Actual(ClaimsPrincipal principal, bool carga = false)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) throw new UnauthorizedAccessException();
        await using var db = factory.Auth();
        var usuario = await db.QuerySingleOrDefaultAsync<UsuarioInfo>($"SELECT {Columnas} {Desde} WHERE u.ID_USUARIO = @id AND u.HABILITADO = 1 AND r.HABILITADO = 1 AND (r.NACIONAL_OBLIGATORIO = 0 OR u.ES_NACIONAL = 1);", new { id }) ?? throw new UnauthorizedAccessException();
        if (carga && !usuario.PuedeCargar) throw new UnauthorizedAccessException("Su perfil permite únicamente consultar.");
        if (!usuario.PuedeAdministrarUsuarios && !await db.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.ING_USUARIO_MODULO um JOIN dbo.ING_MODULO m ON m.CLAVE = um.CLAVE WHERE um.ID_USUARIO = @id AND m.CLAVE = @modulo AND m.HABILITADO = 1) THEN 1 ELSE 0 END;", new { id, modulo = catalogos.Modulo })) throw new UnauthorizedAccessException("No tiene acceso al módulo DDCP.");
        if (!await db.ExecuteScalarAsync<bool>("SELECT HABILITADO FROM dbo.ING_MODULO WHERE CLAVE = @modulo;", new { modulo = catalogos.Modulo })) throw new UnauthorizedAccessException("El módulo DDCP está deshabilitado.");
        return usuario;
    }
    public async Task<int> Crear(UsuarioRequest request)
    {
        await using var db = factory.Auth();
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        var rol = await ObtenerRol(request.Rol, db, tx);
        UsuarioValidator.Validar(request, true, Reglas, rol);
        if (request.IdEntidad.HasValue && !await db.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.CAT_ENTIDAD WHERE ID_ENTIDAD = @id AND HABILITADO = 1) THEN 1 ELSE 0 END;", new { id = request.IdEntidad }, tx))
        {
            throw new ArgumentException("Entidad inexistente o deshabilitada.");
        }
        try
        {
            var id = await db.ExecuteScalarAsync<int>("INSERT dbo.ING_USUARIO(USUARIO, NOMBRE, PASSWORD_HASH, ROL, ES_NACIONAL, ID_ENTIDAD) OUTPUT inserted.ID_USUARIO VALUES (@Usuario, @Nombre, @hash, @Rol, @EsNacional, @IdEntidad);", new { Usuario = request.Usuario.Trim(), Nombre = request.Nombre.Trim(), hash = BCrypt.Net.BCrypt.HashPassword(request.Password, Reglas.BCryptWorkFactor), request.Rol, request.EsNacional, request.IdEntidad }, tx);
            await db.ExecuteAsync("INSERT dbo.ING_USUARIO_MODULO(ID_USUARIO, CLAVE) VALUES (@id, @modulo);", new { id, modulo = catalogos.Modulo }, tx);
            await tx.CommitAsync();
            return id;
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            throw new ArgumentException("El nombre de usuario ya está registrado.");
        }
    }
    public async Task Editar(int id, UsuarioRequest request, int actor)
    {
        await using var db = factory.Auth();
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        var rol = await ObtenerRol(request.Rol, db, tx);
        UsuarioValidator.Validar(request, false, Reglas, rol);
        if (id == actor && !rol.PuedeAdministrarUsuarios) throw new ArgumentException("No puede quitarse su propio permiso de administración.");
        var actuales = (await db.QueryAsync<UsuarioInfo>($"SELECT {Columnas} FROM dbo.ING_USUARIO u WITH (UPDLOCK, HOLDLOCK) JOIN dbo.CAT_ROL r WITH (HOLDLOCK) ON r.CLAVE = u.ROL ORDER BY u.ID_USUARIO;", transaction: tx)).ToList();
        var actual = actuales.SingleOrDefault(x => x.IdUsuario == id) ?? throw new ArgumentException("Usuario inexistente.");
        if (!actual.Habilitado)
        {
            throw new ArgumentException("Reactive el usuario antes de editarlo.");
        }
        if (actual.PuedeAdministrarUsuarios && !rol.PuedeAdministrarUsuarios && actuales.Count(x => x.Habilitado && x.PuedeAdministrarUsuarios) <= 1)
        {
            throw new ArgumentException("Debe conservar al menos un administrador activo.");
        }
        if (request.IdEntidad.HasValue && !await db.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.CAT_ENTIDAD WHERE ID_ENTIDAD = @id AND HABILITADO = 1) THEN 1 ELSE 0 END;", new { id = request.IdEntidad }, tx))
        {
            throw new ArgumentException("Entidad inexistente o deshabilitada.");
        }
        var hash = string.IsNullOrEmpty(request.Password) ? actual.PasswordHash : BCrypt.Net.BCrypt.HashPassword(request.Password, Reglas.BCryptWorkFactor);
        try
        {
            await db.ExecuteAsync("UPDATE dbo.ING_USUARIO SET USUARIO = @Usuario, NOMBRE = @Nombre, PASSWORD_HASH = @hash, ROL = @Rol, ES_NACIONAL = @EsNacional, ID_ENTIDAD = @IdEntidad WHERE ID_USUARIO = @id;", new { id, Usuario = request.Usuario.Trim(), Nombre = request.Nombre.Trim(), hash, request.Rol, request.EsNacional, request.IdEntidad }, tx);
            await tx.CommitAsync();
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            throw new ArgumentException("El nombre de usuario ya está registrado.");
        }
    }
    public async Task CambiarEstado(int id, bool habilitado, int actor)
    {
        if (id == actor && !habilitado)
        {
            throw new ArgumentException("No puede deshabilitar su propia cuenta.");
        }
        await using var db = factory.Auth();
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        var actuales = (await db.QueryAsync<UsuarioInfo>($"SELECT {Columnas} FROM dbo.ING_USUARIO u WITH (UPDLOCK, HOLDLOCK) JOIN dbo.CAT_ROL r WITH (HOLDLOCK) ON r.CLAVE = u.ROL ORDER BY u.ID_USUARIO;", transaction: tx)).ToList();
        var actual = actuales.SingleOrDefault(x => x.IdUsuario == id) ?? throw new ArgumentException("Usuario inexistente.");
        if (!habilitado && actual.Habilitado && actual.PuedeAdministrarUsuarios && actuales.Count(x => x.Habilitado && x.PuedeAdministrarUsuarios) <= 1)
        {
            throw new ArgumentException("Debe conservar al menos un administrador activo.");
        }
        await db.ExecuteAsync("UPDATE dbo.ING_USUARIO SET HABILITADO = @habilitado WHERE ID_USUARIO = @id;", new { id, habilitado }, tx);
        await tx.CommitAsync();
    }
    public async Task Bootstrap()
    {
        await using var db = factory.Auth();
        if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ING_USUARIO;") != 0) throw new ArgumentException("El alta inicial solo se permite cuando no hay usuarios.");
        var nombre = Environment.GetEnvironmentVariable("INGESTA_ADMIN_USUARIO") ?? throw new ArgumentException("Defina INGESTA_ADMIN_USUARIO.");
        var password = Environment.GetEnvironmentVariable("INGESTA_ADMIN_PASSWORD") ?? throw new ArgumentException("Defina INGESTA_ADMIN_PASSWORD para crear el superusuario.");
        var rol = (await catalogos.Roles()).FirstOrDefault(x => x.PuedeAdministrarUsuarios && x.NacionalObligatorio)
            ?? throw new ArgumentException("El catálogo no contiene un rol administrador nacional habilitado.");
        var descripcion = Environment.GetEnvironmentVariable("INGESTA_ADMIN_NOMBRE") ?? throw new ArgumentException("Defina INGESTA_ADMIN_NOMBRE.");
        await Crear(new UsuarioRequest(nombre, descripcion, password, rol.Clave, true, null));
    }
    public async Task<List<UsuarioInfo>> Listar()
    {
        await using var db = factory.Auth();
        return (await db.QueryAsync<UsuarioInfo>($"SELECT {Columnas} {Desde} ORDER BY u.USUARIO;")).ToList();
    }
    public async Task<(List<UsuarioInfo> Usuarios, Dictionary<int, string> Entidades)> DatosExportacion()
    {
        await using var db = factory.Auth();
        var lista = (await db.QueryAsync<UsuarioInfo>($"SELECT {Columnas} {Desde} ORDER BY u.USUARIO;")).ToList();
        var entidades = (await db.QueryAsync<Entidad>("SELECT ID_ENTIDAD IdEntidad, NOMBRE_ENTIDAD NombreEntidad, NOMBRE_CORTO NombreCorto FROM dbo.CAT_ENTIDAD;")).ToDictionary(x => x.IdEntidad, x => x.NombreEntidad);
        return (lista, entidades);
    }
}

