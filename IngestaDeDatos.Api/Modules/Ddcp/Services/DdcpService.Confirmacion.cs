using ClosedXML.Excel;
using Dapper;
using IngestaDeDatos.Api.Data;
using IngestaDeDatos.Api.Models;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IngestaDeDatos.Api.Services;

public sealed partial class DdcpService
{    public async Task Confirmar(UsuarioInfo usuario, Guid id)
    {
        await using var db = factory.Modulo("DDCP");
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        var carga = await db.QuerySingleOrDefaultAsync<CargaInfo>(DdcpSql.ObtenerCargaParaConfirmar, new { id }, tx) ?? throw new ArgumentException("La carga no existe.");
        if (carga.IdUsuario != usuario.IdUsuario) throw new UnauthorizedAccessException("La carga pertenece a otro usuario.");
        if (carga.Estado != "PREVIA") throw new ArgumentException("La carga ya fue confirmada o cancelada.");
        var filas = JsonSerializer.Deserialize<List<FilaDdcp>>(carga.FilasJson) ?? throw new ArgumentException("Carga sin filas.");
        foreach (var fila in filas.OrderBy(x => x.IdEntidad))
        {
            if (!usuario.EsNacional && fila.IdEntidad != usuario.IdEntidad) throw new UnauthorizedAccessException("Su alcance cambió. Prepare otra carga.");
            if (!await db.ExecuteScalarAsync<bool>(DdcpSql.EntidadHabilitada, new { entidad = fila.IdEntidad }, tx)) throw new ArgumentException("Una entidad de la carga fue deshabilitada. Prepare otra carga.");
            var actual = await db.QuerySingleOrDefaultAsync<RegistroDdcp>(DdcpSql.ObtenerRegistroParaConfirmar, new { entidad = fila.IdEntidad, periodo = carga.Periodo }, tx);
            if (actual?.Revision != fila.RevisionAntes || actual?.Dispositivos.ToString("0", CultureInfo.InvariantCulture) != fila.DispositivosAntes || actual?.Personas != fila.PersonasAntes || actual?.Habilitado != fila.HabilitadoAntes) throw new ArgumentException("Los datos cambiaron después de la vista previa. Cancele esta carga y vuelva a validar el archivo.");
            var parametros = new { entidad = fila.IdEntidad, periodo = carga.Periodo, dispositivos = decimal.Parse(fila.Dispositivos, CultureInfo.InvariantCulture), personas = fila.Personas, nombre = fila.NombreEntidad, id, usuario = usuario.IdUsuario };
            if (actual is null)
            {
                await db.ExecuteAsync(DdcpSql.InsertarRegistro, parametros, tx);
            }
            else if (actual.Dispositivos != parametros.dispositivos || actual.Personas != fila.Personas || !actual.Habilitado)
            {
                await db.ExecuteAsync(DdcpSql.GuardarHistorico, parametros, tx);
                await db.ExecuteAsync(DdcpSql.ActualizarRegistro, parametros, tx);
            }
        }
        await db.ExecuteAsync(DdcpSql.ConfirmarCarga, new { id }, tx);
        await tx.CommitAsync();
    }
    public async Task Cancelar(UsuarioInfo usuario, Guid id)
    {
        await using var db = factory.Modulo("DDCP");
        if (await db.ExecuteAsync(DdcpSql.CancelarCarga, new { id, usuario = usuario.IdUsuario }) != 1) throw new ArgumentException("La carga no existe, pertenece a otro usuario o ya terminó.");
    }

}
