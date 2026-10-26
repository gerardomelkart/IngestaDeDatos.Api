using IngestaDeDatos.Api.Configuration;
using IngestaDeDatos.Api.Repositories;
using IngestaDeDatos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using System.Globalization;

namespace IngestaDeDatos.Api.Controllers;

[Authorize, ApiController, Route("api/catalogos")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class CatalogosController(ICatalogosRepository catalogos, UsuariosService usuarios, IOptions<ReglasDdcpOptions> reglas) : ControllerBase
{
    [AllowAnonymous, HttpGet("/api/sistema")]
    public async Task<IActionResult> Sistema()
    {
        return Ok(new { modulo = catalogos.Modulo, nombreModulo = await catalogos.NombreModulo() });
    }
    [HttpGet]
    public async Task<IActionResult> Obtener()
    {
        var usuario = await usuarios.Actual(User);
        var r = reglas.Value;
        return Ok(new {
            modulo = catalogos.Modulo,
            nombreModulo = await catalogos.NombreModulo(),
            roles = await catalogos.Roles(), meses = await catalogos.Meses(),
            usuario = new { usuario.PuedeCargar, usuario.PuedeAdministrarUsuarios },
            reglas = new { r.AnioMinimo, r.AnioMaximo, r.MaxArchivoBytes, maxDispositivos = r.MaxDispositivos.ToString("0", CultureInfo.InvariantCulture), r.MaxPersonas, r.UsuarioMaximo, r.NombreMaximo, r.PasswordMinimo, r.PasswordMaximoBytes, r.FilasPagina }
        });
    }
}
