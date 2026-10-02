using Dapper;
using IngestaDeDatos.Api.Data;
using IngestaDeDatos.Api.Models;
using IngestaDeDatos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IngestaDeDatos.Api.Controllers;

[Authorize, ApiController, Route("api/usuarios")]
public sealed class UsuariosController(UsuariosService usuarios, DbFactory factory) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        if ((await usuarios.Actual(User)).Rol != "SUPER_USUARIO") throw new UnauthorizedAccessException();
        await using var db = factory.Auth();
        var lista = await db.QueryAsync<UsuarioInfo>("SELECT ID_USUARIO IdUsuario, USUARIO Usuario, NOMBRE Nombre, ROL Rol, ES_NACIONAL EsNacional, ID_ENTIDAD IdEntidad FROM dbo.ING_USUARIO ORDER BY USUARIO;");
        return Ok(lista.Select(x => new { x.IdUsuario, x.Usuario, x.Nombre, x.Rol, x.EsNacional, x.IdEntidad }));
    }
    [HttpPost]
    public async Task<IActionResult> Crear(UsuarioRequest request)
    {
        if ((await usuarios.Actual(User)).Rol != "SUPER_USUARIO") throw new UnauthorizedAccessException();
        return Ok(new { idUsuario = await usuarios.Crear(request) });
    }
}
