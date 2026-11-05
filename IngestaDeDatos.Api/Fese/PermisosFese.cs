using Dapper;
using IngestaDeDatos.Api.Data;
using IngestaDeDatos.Api.Models;
using System.Data;

namespace IngestaDeDatos.Api.Fese;

public sealed record PermisoFese(bool HabilitaCarga, bool HabilitaActualizacion);
public sealed record OpcionFese(string Clave, bool Habilitada, long Version);
public sealed record PermisoCuentaFese(int IdUsuario, bool HabilitaCarga, bool HabilitaActualizacion, long Version);

public sealed record ExcepcionRecepcionFese(int IdUsuario, string Clave);

public sealed class PermisosFese(DbFactory factory)
{
    public async Task<List<OpcionFese>> Generales()
    {
        await using var db = factory.Modulo("FESE");
        return (await db.QueryAsync<OpcionFese>("SELECT CLAVE Clave, HABILITADA Habilitada, VERSION Version FROM dbo.FESE_OPERACION ORDER BY CLAVE")).ToList();
    }
    public async Task<List<PermisoCuentaFese>> Cuentas()
    {
        await using var db = factory.Modulo("FESE");
        return (await db.QueryAsync<PermisoCuentaFese>("SELECT ID_USUARIO IdUsuario, HABILITA_CARGA HabilitaCarga, HABILITA_ACTUALIZACION HabilitaActualizacion, VERSION Version FROM dbo.FESE_PERMISO_USUARIO")).ToList();
    }
    public async Task<List<ExcepcionRecepcionFese>> Excepciones()
    {
        await using var db = factory.Modulo("FESE");
        return (await db.QueryAsync<ExcepcionRecepcionFese>(
            "SELECT ID_USUARIO IdUsuario, CLAVE Clave FROM dbo.FESE_EXCEPCION_RECEPCION")).ToList();
    }
    public async Task<PermisoFese> Efectivos(UsuarioInfo usuario)
    {
        if (!usuario.PuedeCargar) return new(false, false);
        await using var db = factory.Modulo("FESE");
        return await db.QuerySingleAsync<PermisoFese>("""
            SELECT dbo.FESE_PERMITE_OPERACION(@id, 'CARGA') HabilitaCarga,
                   dbo.FESE_PERMITE_OPERACION(@id, 'ACTUALIZACION') HabilitaActualizacion
            """, new { id = usuario.IdUsuario });
    }
    public async Task<bool> CambiarExcepcion(int id, string clave, bool habilitada, long version, int actor)
    {
        await using var db = factory.Modulo("FESE");
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        var operacion = await db.QuerySingleAsync<OpcionFese>(
            "SELECT CLAVE Clave, HABILITADA Habilitada, VERSION Version FROM dbo.FESE_OPERACION WITH (UPDLOCK, HOLDLOCK) WHERE CLAVE=@clave",
            new { clave }, tx);
        if (operacion.Habilitada || operacion.Version != version) return false;

        var anterior = await db.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.FESE_EXCEPCION_RECEPCION WHERE CLAVE=@clave AND ID_USUARIO=@id",
            new { clave, id }, tx) > 0;
        if (anterior != habilitada)
        {
            await db.ExecuteAsync(habilitada
                ? "INSERT dbo.FESE_EXCEPCION_RECEPCION(CLAVE,ID_USUARIO) VALUES(@clave,@id)"
                : "DELETE dbo.FESE_EXCEPCION_RECEPCION WHERE CLAVE=@clave AND ID_USUARIO=@id", new { clave, id }, tx);
            await db.ExecuteAsync("""
                INSERT dbo.FESE_PERMISO_BITACORA(ID_USUARIO_ACTOR,ID_USUARIO_OBJETIVO,OPERACION,VALOR_ANTERIOR,VALOR_NUEVO,FECHA)
                VALUES(@actor,@id,@evento,@anterior,@habilitada,@fecha)
                """, new { actor, id, evento = "EXC_" + clave, anterior, habilitada, fecha = RepositorioFese.Ahora() }, tx);
        }
        await tx.CommitAsync();
        return true;
    }
    public async Task<bool> CambiarGeneral(string clave, bool habilitada, long version, int actor)
    {
        await using var db = factory.Modulo("FESE");
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        var actual = await db.QuerySingleAsync<OpcionFese>("SELECT CLAVE Clave, HABILITADA Habilitada, VERSION Version FROM dbo.FESE_OPERACION WITH (UPDLOCK, HOLDLOCK) WHERE CLAVE=@clave", new { clave }, tx);
        if (actual.Version != version) return false;
        if (actual.Habilitada == habilitada) return true;
        await db.ExecuteAsync("UPDATE dbo.FESE_OPERACION SET HABILITADA=@habilitada, VERSION=VERSION+1 WHERE CLAVE=@clave", new { habilitada, clave }, tx);
        await db.ExecuteAsync("DELETE dbo.FESE_EXCEPCION_RECEPCION WHERE CLAVE=@clave", new { clave }, tx);
        await db.ExecuteAsync("INSERT dbo.FESE_PERMISO_BITACORA(ID_USUARIO_ACTOR,OPERACION,VALOR_ANTERIOR,VALOR_NUEVO,FECHA) VALUES(@actor,@clave,@anterior,@habilitada,@fecha)", new { actor, clave, anterior = actual.Habilitada, habilitada, fecha = RepositorioFese.Ahora() }, tx);
        await tx.CommitAsync();
        return true;
    }
    public async Task<bool> CambiarCuenta(int id, PermisoFese permiso, long version, int actor)
    {
        await using var db = factory.Modulo("FESE");
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        var actual = await db.QuerySingleOrDefaultAsync<PermisoCuentaFese>("SELECT ID_USUARIO IdUsuario, HABILITA_CARGA HabilitaCarga, HABILITA_ACTUALIZACION HabilitaActualizacion, VERSION Version FROM dbo.FESE_PERMISO_USUARIO WITH (UPDLOCK, HOLDLOCK) WHERE ID_USUARIO=@id", new { id }, tx);
        if ((actual?.Version ?? 0) != version) return false;
        if (actual is null)
            await db.ExecuteAsync("INSERT dbo.FESE_PERMISO_USUARIO(ID_USUARIO,HABILITA_CARGA,HABILITA_ACTUALIZACION) VALUES(@id,@HabilitaCarga,@HabilitaActualizacion)", new { id, permiso.HabilitaCarga, permiso.HabilitaActualizacion }, tx);
        else
            await db.ExecuteAsync("UPDATE dbo.FESE_PERMISO_USUARIO SET HABILITA_CARGA=@HabilitaCarga,HABILITA_ACTUALIZACION=@HabilitaActualizacion,VERSION=VERSION+1 WHERE ID_USUARIO=@id", new { id, permiso.HabilitaCarga, permiso.HabilitaActualizacion }, tx);
        foreach (var cambio in new[] { (Clave: "CARGA", Anterior: actual?.HabilitaCarga ?? true, Nuevo: permiso.HabilitaCarga), (Clave: "ACTUALIZACION", Anterior: actual?.HabilitaActualizacion ?? true, Nuevo: permiso.HabilitaActualizacion) })
            if (cambio.Anterior != cambio.Nuevo)
                await db.ExecuteAsync("INSERT dbo.FESE_PERMISO_BITACORA(ID_USUARIO_ACTOR,ID_USUARIO_OBJETIVO,OPERACION,VALOR_ANTERIOR,VALOR_NUEVO,FECHA) VALUES(@actor,@id,@Clave,@Anterior,@Nuevo,@fecha)", new { actor, id, cambio.Clave, cambio.Anterior, cambio.Nuevo, fecha = RepositorioFese.Ahora() }, tx);
        await tx.CommitAsync();
        return true;
    }
}
