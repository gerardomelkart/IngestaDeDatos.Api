using Dapper;
using IngestaDeDatos.Api.Data;
using IngestaDeDatos.Api.Models;
using System.Security.Claims;
using System.Data;
using Microsoft.Data.SqlClient;

namespace IngestaDeDatos.Api.Services;

public sealed class UsuariosService(DbFactory factory)
{
    private const string Columnas = "ID_USUARIO IdUsuario, USUARIO Usuario, NOMBRE Nombre, PASSWORD_HASH PasswordHash, ROL Rol, ES_NACIONAL EsNacional, ID_ENTIDAD IdEntidad, HABILITADO Habilitado";
    public async Task<UsuarioInfo?> Buscar(string usuario)
    {
        await using var db = factory.Auth();
        return await db.QuerySingleOrDefaultAsync<UsuarioInfo>($"SELECT {Columnas} FROM dbo.ING_USUARIO WHERE USUARIO = @usuario AND HABILITADO = 1;", new { usuario });
    }
    public async Task<UsuarioInfo> Actual(ClaimsPrincipal principal, bool carga = false)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id)) throw new UnauthorizedAccessException();
        await using var db = factory.Auth();
        var usuario = await db.QuerySingleOrDefaultAsync<UsuarioInfo>($"SELECT {Columnas} FROM dbo.ING_USUARIO WHERE ID_USUARIO = @id AND HABILITADO = 1;", new { id }) ?? throw new UnauthorizedAccessException();
        if (carga && usuario.Rol == "CONSULTA") throw new UnauthorizedAccessException("Su perfil permite únicamente consultar.");
        if (usuario.Rol != "SUPER_USUARIO" && !await db.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.ING_USUARIO_MODULO um JOIN dbo.ING_MODULO m ON m.CLAVE = um.CLAVE WHERE um.ID_USUARIO = @id AND m.CLAVE = 'DDCP' AND m.HABILITADO = 1) THEN 1 ELSE 0 END;", new { id })) throw new UnauthorizedAccessException("No tiene acceso al módulo DDCP.");
        if (!await db.ExecuteScalarAsync<bool>("SELECT HABILITADO FROM dbo.ING_MODULO WHERE CLAVE = 'DDCP';")) throw new UnauthorizedAccessException("El módulo DDCP está deshabilitado.");
        return usuario;
    }
    private static void Validar(UsuarioRequest request, bool nuevo)
    {
        if (string.IsNullOrWhiteSpace(request.Usuario) || request.Usuario.Trim().Length > 80 || string.IsNullOrWhiteSpace(request.Nombre) || request.Nombre.Trim().Length > 160)
        {
            throw new ArgumentException("Capture usuario (máximo 80 caracteres) y nombre (máximo 160).");
        }
        if ((nuevo || !string.IsNullOrEmpty(request.Password)) && (string.IsNullOrEmpty(request.Password) || request.Password.Length < 8 || System.Text.Encoding.UTF8.GetByteCount(request.Password) > 72))
        {
            throw new ArgumentException("La contraseña requiere al menos 8 caracteres y un máximo de 72 bytes.");
        }
        if (!new[] { "SUPER_USUARIO", "ENLACE_ESTATAL", "CONSULTA" }.Contains(request.Rol))
        {
            throw new ArgumentException("Rol inválido.");
        }
        if (request.Rol == "SUPER_USUARIO" && !request.EsNacional)
        {
            throw new ArgumentException("El superusuario siempre debe ser nacional.");
        }
        if (request.EsNacional == request.IdEntidad.HasValue)
        {
            throw new ArgumentException("El usuario nacional no lleva entidad; el usuario por entidad requiere una.");
        }
    }
    public async Task<int> Crear(UsuarioRequest request)
    {
        Validar(request, true);
        await using var db = factory.Auth();
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        if (request.IdEntidad.HasValue && !await db.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.CAT_ENTIDAD WHERE ID_ENTIDAD = @id AND HABILITADO = 1) THEN 1 ELSE 0 END;", new { id = request.IdEntidad }, tx))
        {
            throw new ArgumentException("Entidad inexistente o deshabilitada.");
        }
        try
        {
            var id = await db.ExecuteScalarAsync<int>("INSERT dbo.ING_USUARIO(USUARIO, NOMBRE, PASSWORD_HASH, ROL, ES_NACIONAL, ID_ENTIDAD) OUTPUT inserted.ID_USUARIO VALUES (@Usuario, @Nombre, @hash, @Rol, @EsNacional, @IdEntidad);", new { Usuario = request.Usuario.Trim(), Nombre = request.Nombre.Trim(), hash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12), request.Rol, request.EsNacional, request.IdEntidad }, tx);
            await db.ExecuteAsync("INSERT dbo.ING_USUARIO_MODULO(ID_USUARIO, CLAVE) VALUES (@id, 'DDCP');", new { id }, tx);
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
        Validar(request, false);
        if (id == actor && request.Rol != "SUPER_USUARIO")
        {
            throw new ArgumentException("No puede quitarse su propio rol de superusuario.");
        }
        await using var db = factory.Auth();
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        var actuales = (await db.QueryAsync<UsuarioInfo>($"SELECT {Columnas} FROM dbo.ING_USUARIO WITH (UPDLOCK, HOLDLOCK) ORDER BY ID_USUARIO;", transaction: tx)).ToList();
        var actual = actuales.SingleOrDefault(x => x.IdUsuario == id) ?? throw new ArgumentException("Usuario inexistente.");
        if (!actual.Habilitado)
        {
            throw new ArgumentException("Reactive el usuario antes de editarlo.");
        }
        if (actual.Rol == "SUPER_USUARIO" && request.Rol != "SUPER_USUARIO" && actuales.Count(x => x.Habilitado && x.Rol == "SUPER_USUARIO") <= 1)
        {
            throw new ArgumentException("Debe conservar al menos un superusuario activo.");
        }
        if (request.IdEntidad.HasValue && !await db.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.CAT_ENTIDAD WHERE ID_ENTIDAD = @id AND HABILITADO = 1) THEN 1 ELSE 0 END;", new { id = request.IdEntidad }, tx))
        {
            throw new ArgumentException("Entidad inexistente o deshabilitada.");
        }
        var hash = string.IsNullOrEmpty(request.Password) ? actual.PasswordHash : BCrypt.Net.BCrypt.HashPassword(request.Password, 12);
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
        var actuales = (await db.QueryAsync<UsuarioInfo>($"SELECT {Columnas} FROM dbo.ING_USUARIO WITH (UPDLOCK, HOLDLOCK) ORDER BY ID_USUARIO;", transaction: tx)).ToList();
        var actual = actuales.SingleOrDefault(x => x.IdUsuario == id) ?? throw new ArgumentException("Usuario inexistente.");
        if (!habilitado && actual.Habilitado && actual.Rol == "SUPER_USUARIO" && actuales.Count(x => x.Habilitado && x.Rol == "SUPER_USUARIO") <= 1)
        {
            throw new ArgumentException("Debe conservar al menos un superusuario activo.");
        }
        await db.ExecuteAsync("UPDATE dbo.ING_USUARIO SET HABILITADO = @habilitado WHERE ID_USUARIO = @id;", new { id, habilitado }, tx);
        await tx.CommitAsync();
    }
    public async Task Bootstrap()
    {
        await using var db = factory.Auth();
        if (await db.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM dbo.ING_USUARIO;") != 0) throw new ArgumentException("El alta inicial solo se permite cuando no hay usuarios.");
        var nombre = Environment.GetEnvironmentVariable("INGESTA_ADMIN_USUARIO") ?? "admin";
        var password = Environment.GetEnvironmentVariable("INGESTA_ADMIN_PASSWORD") ?? throw new ArgumentException("Defina INGESTA_ADMIN_PASSWORD para crear el superusuario.");
        await Crear(new UsuarioRequest(nombre, "Administrador nacional", password, "SUPER_USUARIO", true, null));
    }
}

