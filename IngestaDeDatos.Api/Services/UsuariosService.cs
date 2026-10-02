using Dapper;
using IngestaDeDatos.Api.Data;
using IngestaDeDatos.Api.Models;
using System.Security.Claims;

namespace IngestaDeDatos.Api.Services;

public sealed class UsuariosService(DbFactory factory)
{
    private const string Columnas = "ID_USUARIO IdUsuario, USUARIO Usuario, NOMBRE Nombre, PASSWORD_HASH PasswordHash, ROL Rol, ES_NACIONAL EsNacional, ID_ENTIDAD IdEntidad";
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
    public async Task<int> Crear(UsuarioRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Usuario) || request.Usuario.Trim().Length > 80 || string.IsNullOrWhiteSpace(request.Nombre) || request.Nombre.Trim().Length > 160 || string.IsNullOrEmpty(request.Password) || request.Password.Length < 8 || System.Text.Encoding.UTF8.GetByteCount(request.Password) > 72) throw new ArgumentException("Capture usuario, nombre y contraseña de 8 caracteres o más (máximo 72 bytes).");
        if (!new[] { "SUPER_USUARIO", "ENLACE_ESTATAL", "CONSULTA" }.Contains(request.Rol)) throw new ArgumentException("Rol inválido.");
        if (request.Rol == "SUPER_USUARIO" && !request.EsNacional) throw new ArgumentException("El superusuario siempre debe ser nacional.");
        if (request.EsNacional == request.IdEntidad.HasValue) throw new ArgumentException("El usuario nacional no lleva entidad; el estatal requiere una.");
        await using var db = factory.Auth();
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync();
        if (request.IdEntidad.HasValue && !await db.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.CAT_ENTIDAD WHERE ID_ENTIDAD = @id AND HABILITADO = 1) THEN 1 ELSE 0 END;", new { id = request.IdEntidad }, tx)) throw new ArgumentException("Entidad inexistente o deshabilitada.");
        var id = await db.ExecuteScalarAsync<int>("INSERT dbo.ING_USUARIO(USUARIO, NOMBRE, PASSWORD_HASH, ROL, ES_NACIONAL, ID_ENTIDAD) OUTPUT inserted.ID_USUARIO VALUES (@Usuario, @Nombre, @hash, @Rol, @EsNacional, @IdEntidad);", new { Usuario = request.Usuario.Trim(), Nombre = request.Nombre.Trim(), hash = BCrypt.Net.BCrypt.HashPassword(request.Password, 12), request.Rol, request.EsNacional, request.IdEntidad }, tx);
        await db.ExecuteAsync("INSERT dbo.ING_USUARIO_MODULO(ID_USUARIO, CLAVE) VALUES (@id, 'DDCP');", new { id }, tx);
        await tx.CommitAsync();
        return id;
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
