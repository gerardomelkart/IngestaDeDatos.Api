using IngestaDeDatos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Globalization;

namespace IngestaDeDatos.Api.Controllers;

[Authorize, ApiController, Route("api/ddcp")]
public sealed class DdcpController(UsuariosService usuarios, DdcpService ddcp) : ControllerBase
{
    [HttpGet("entidades")]
    public async Task<IActionResult> Entidades() => Ok(await ddcp.Entidades(await usuarios.Actual(User)));
    [HttpGet("datos")]
    public async Task<IActionResult> Datos(int anio, int? mes, int? idEntidad)
    {
        var filas = await ddcp.Consultar(await usuarios.Actual(User), anio, mes, idEntidad);
        return Ok(new { filas = filas.Select(x => new { x.IdEntidad, x.NombreEntidad, anio = x.Periodo.Year, mes = x.Periodo.Month, dispositivos = x.Dispositivos.ToString("0", CultureInfo.InvariantCulture), x.Personas, x.Revision }), totalDispositivos = filas.Sum(x => x.Dispositivos).ToString("0", CultureInfo.InvariantCulture), totalPersonas = filas.Sum(x => (long)x.Personas) });
    }
    [HttpGet("plantilla")]
    public async Task<IActionResult> Plantilla(int anio, int mes) => File(await ddcp.Plantilla(await usuarios.Actual(User, true), anio, mes), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"DDCP_{anio}_{mes:00}.xlsx");
    [HttpPost("cargas/validar"), RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<IActionResult> Validar([FromForm] int anio, [FromForm] int mes, [FromForm] string hoja, IFormFile archivo) => Ok(await ddcp.Preparar(await usuarios.Actual(User, true), anio, mes, hoja, archivo));
    [HttpPost("cargas/{id:guid}/confirmar")]
    public async Task<IActionResult> Confirmar(Guid id) { await ddcp.Confirmar(await usuarios.Actual(User, true), id); return Ok(new { mensaje = "Carga confirmada. Los datos están disponibles para consulta." }); }
    [HttpPost("cargas/{id:guid}/cancelar")]
    public async Task<IActionResult> Cancelar(Guid id) { await ddcp.Cancelar(await usuarios.Actual(User, true), id); return Ok(new { mensaje = "Carga cancelada." }); }
}
