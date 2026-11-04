using Dapper;
using IngestaDeDatos.Api.Data;
using IngestaDeDatos.Api.Models;
using System.Security.Claims;

namespace IngestaDeDatos.Api.Fese;

public sealed class AccesoFese(DbFactory factory)
{
    public async Task<UsuarioInfo> Actual(ClaimsPrincipal principal, bool carga = false, bool administra = false)
    {
        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
            throw new UnauthorizedAccessException("Inicie sesión nuevamente.");
        await using var db = factory.Auth();
        var usuario = await db.QuerySingleOrDefaultAsync<UsuarioInfo>("""
            SELECT u.ID_USUARIO IdUsuario, u.USUARIO Usuario, u.NOMBRE Nombre, r.CLAVE Rol,
                   u.ES_NACIONAL EsNacional, u.ID_ENTIDAD IdEntidad, r.PUEDE_CARGAR PuedeCargar,
                   r.PUEDE_ADMINISTRAR_USUARIOS PuedeAdministrarUsuarios
            FROM dbo.ING_USUARIO u JOIN dbo.CAT_ROL r ON r.CLAVE = u.ROL
            WHERE u.ID_USUARIO = @id AND u.HABILITADO = 1 AND r.HABILITADO = 1
              AND (r.NACIONAL_OBLIGATORIO = 0 OR u.ES_NACIONAL = 1)
              AND EXISTS (SELECT 1 FROM dbo.ING_MODULO WHERE CLAVE = 'FESE' AND HABILITADO = 1)
              AND ((r.PUEDE_ADMINISTRAR_USUARIOS = 1 AND u.ES_NACIONAL = 1)
                   OR EXISTS (SELECT 1 FROM dbo.ING_USUARIO_MODULO WHERE ID_USUARIO = @id AND CLAVE = 'FESE'));
            """, new { id }) ?? throw new UnauthorizedAccessException("No tiene acceso habilitado a FESE.");
        if (carga && !usuario.PuedeCargar) throw new UnauthorizedAccessException("Su perfil permite únicamente consultar.");
        if (administra && (!usuario.PuedeAdministrarUsuarios || !usuario.EsNacional))
            throw new UnauthorizedAccessException("Se requiere un superusuario nacional.");
        return usuario;
    }

    public static int Entidad(UsuarioInfo usuario, int? entidad)
    {
        var id = usuario.EsNacional ? entidad : usuario.IdEntidad;
        if (id is null || id < 1 || id > 32) throw new ArgumentException("Seleccione una entidad válida.");
        if (!usuario.EsNacional && entidad.HasValue && entidad != usuario.IdEntidad)
            throw new UnauthorizedAccessException("No tiene acceso a otra entidad.");
        return id.Value;
    }
}
