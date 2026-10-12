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

public sealed partial class DdcpService(DbFactory factory)
{
    private static readonly string[] Meses = ["", "ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE"];
    private const string ConsultaSql = DdcpSql.ConsultarRegistros;
    public static void PeriodoValido(int anio, int? mes)
    {
        if (anio < 2000 || anio > 2100 || mes.HasValue && (mes < 1 || mes > 12)) throw new ArgumentException("Periodo inválido: año 2000 a 2100 y mes 1 a 12.");
    }
    public async Task<List<Entidad>> Entidades(UsuarioInfo usuario)
    {
        await using var db = factory.Modulo("DDCP");
        return (await db.QueryAsync<Entidad>(DdcpSql.ConsultarEntidades, new { id = usuario.EsNacional ? null : usuario.IdEntidad })).ToList();
    }
    public async Task<List<RegistroDdcp>> Consultar(UsuarioInfo usuario, int anio, int? mes, int? entidad, bool incluirInactivos = false)
    {
        PeriodoValido(anio, mes);
        if (!usuario.EsNacional && entidad.HasValue && entidad != usuario.IdEntidad) throw new UnauthorizedAccessException("No puede consultar otra entidad.");
        entidad = usuario.EsNacional ? entidad : usuario.IdEntidad;
        await using var db = factory.Modulo("DDCP");
        var registros = await db.QueryAsync<RegistroDdcp>(ConsultaSql, new { anio, mes, entidad });
        return registros.Where(x => incluirInactivos || x.Habilitado).OrderBy(x => x.Periodo).ThenBy(x => x.IdEntidad).ToList();
    }
    public async Task<byte[]> ExportarConsulta(UsuarioInfo usuario, int anio, int? mes, int? entidad)
    {
        var filas = await Consultar(usuario, anio, mes, entidad);
        var alcance = usuario.EsNacional ? entidad : usuario.IdEntidad;
        return ExportacionDdcp.Generar(filas, anio, mes, alcance);
    }

}
