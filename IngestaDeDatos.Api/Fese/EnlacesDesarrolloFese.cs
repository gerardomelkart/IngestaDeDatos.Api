using Dapper;
using IngestaDeDatos.Api.Configuration;
using IngestaDeDatos.Api.Data;
using IngestaDeDatos.Api.Models;
using IngestaDeDatos.Api.Validators;
using System.Data;

namespace IngestaDeDatos.Api.Fese;

public static class EnlacesDesarrolloFese
{
    public static async Task Crear(DbFactory factory, IHostEnvironment ambiente, ReglasDdcpOptions reglas)
    {
        if (!ambiente.IsDevelopment()) throw new InvalidOperationException("Las cuentas de prueba solo se crean en Development.");
        await using var auth = factory.Auth();
        await using var fese = factory.Modulo("FESE");
        if (!string.Equals(auth.Database, "DDCP_DEV", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("La autenticación debe apuntar a DDCP_DEV. No se hizo ningún cambio.");
        var password = Environment.GetEnvironmentVariable("FESE_ENLACES_PASSWORD") ?? "";
        var entidades = (await fese.QueryAsync<EntidadFese>("SELECT ID_ENTIDAD IdEntidad,NOMBRE Nombre FROM dbo.FESE_ENTIDAD WHERE HABILITADA=1 ORDER BY ID_ENTIDAD")).ToList();
        if (entidades.Count != 32) throw new InvalidOperationException("Se requieren las 32 entidades FESE habilitadas.");
        await auth.OpenAsync();
        await using var tx = await auth.BeginTransactionAsync(IsolationLevel.Serializable);
        var rol = await auth.QuerySingleOrDefaultAsync<RolCatalogo>("SELECT CLAVE Clave,NOMBRE Nombre,PUEDE_CARGAR PuedeCargar,PUEDE_ADMINISTRAR_USUARIOS PuedeAdministrarUsuarios,NACIONAL_OBLIGATORIO NacionalObligatorio FROM dbo.CAT_ROL WHERE CLAVE='ENLACE' AND HABILITADO=1", transaction: tx)
            ?? throw new InvalidOperationException("No existe el rol ENLACE habilitado.");
        if (!rol.PuedeCargar || rol.PuedeAdministrarUsuarios || rol.NacionalObligatorio)
            throw new InvalidOperationException("El rol ENLACE no corresponde a un enlace estatal de carga.");
        var modulo = await auth.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.ING_MODULO WHERE CLAVE='FESE' AND HABILITADO=1) THEN 1 ELSE 0 END", transaction: tx);
        if (!modulo) throw new InvalidOperationException("Primero registre el módulo FESE en DDCP_DEV.");
        var resultados = new List<string>();
        foreach (var entidad in entidades)
        {
            var request = new UsuarioRequest($"fese.enlace.{entidad.IdEntidad:00}", $"Enlace FESE - {entidad.Nombre}", password, rol.Clave, false, entidad.IdEntidad);
            UsuarioValidator.Validar(request, true, reglas, rol);
            if (!await auth.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.CAT_ENTIDAD WHERE ID_ENTIDAD=@IdEntidad AND HABILITADO=1) THEN 1 ELSE 0 END", new { entidad.IdEntidad, entidad.Nombre }, tx))
                throw new InvalidOperationException($"Revise la correspondencia de la entidad {entidad.IdEntidad} con el catálogo central.");
            var existente = await auth.QuerySingleOrDefaultAsync<CuentaFese>("SELECT ID_USUARIO IdUsuario,ROL Rol,ES_NACIONAL EsNacional,ID_ENTIDAD IdEntidad,HABILITADO Habilitado FROM dbo.ING_USUARIO WITH (UPDLOCK,HOLDLOCK) WHERE USUARIO=@Usuario", request, tx);
            if (existente is not null)
            {
                var acceso = await auth.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.ING_USUARIO_MODULO WHERE ID_USUARIO=@IdUsuario AND CLAVE='FESE') THEN 1 ELSE 0 END", existente, tx);
                if (!existente.Habilitado || existente.Rol != rol.Clave || existente.EsNacional || existente.IdEntidad != entidad.IdEntidad || !acceso)
                    throw new InvalidOperationException($"{request.Usuario} ya existe con otra configuración. No se modificó ninguna cuenta.");
                resultados.Add($"{request.Usuario} | {entidad.Nombre} | Existente; contraseña conservada");
                continue;
            }
            var id = await auth.ExecuteScalarAsync<int>("INSERT dbo.ING_USUARIO(USUARIO,NOMBRE,PASSWORD_HASH,ROL,ES_NACIONAL,ID_ENTIDAD) OUTPUT inserted.ID_USUARIO VALUES(@Usuario,@Nombre,@hash,@Rol,0,@IdEntidad)", new { request.Usuario, request.Nombre, request.Rol, request.IdEntidad, hash = BCrypt.Net.BCrypt.HashPassword(password, reglas.BCryptWorkFactor) }, tx);
            await auth.ExecuteAsync("INSERT dbo.ING_USUARIO_MODULO(ID_USUARIO,CLAVE) VALUES(@id,'FESE')", new { id }, tx);
            resultados.Add($"{request.Usuario} | {entidad.Nombre} | Creado");
        }
        await tx.CommitAsync();
        foreach (var resultado in resultados) Console.WriteLine(resultado);
        Console.WriteLine("Enlaces estatales de desarrollo preparados. Sin acceso a DDCP ni administración nacional.");
    }
}
