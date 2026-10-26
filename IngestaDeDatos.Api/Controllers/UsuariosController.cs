using IngestaDeDatos.Api.Models;
using IngestaDeDatos.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IngestaDeDatos.Api.Controllers;

[Authorize, ApiController, Route("api/usuarios")]
public sealed class UsuariosController(UsuariosService usuarios) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Listar()
    {
        if (!(await usuarios.Actual(User)).PuedeAdministrarUsuarios) throw new UnauthorizedAccessException();
        var lista = await usuarios.Listar();
        return Ok(lista.Select(x => new { x.IdUsuario, x.Usuario, x.Nombre, x.Rol, x.EsNacional, x.IdEntidad, x.Habilitado, x.PuedeCargar, x.PuedeAdministrarUsuarios }));
    }
    [HttpPost]
    public async Task<IActionResult> Crear(UsuarioRequest request)
    {
        if (!(await usuarios.Actual(User)).PuedeAdministrarUsuarios) throw new UnauthorizedAccessException();
        return Ok(new { idUsuario = await usuarios.Crear(request) });
    }
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Editar(int id, UsuarioRequest request)
    {
        var actor = await usuarios.Actual(User);
        if (!actor.PuedeAdministrarUsuarios)
        {
            throw new UnauthorizedAccessException();
        }
        await usuarios.Editar(id, request, actor.IdUsuario);
        return Ok(new { mensaje = "Usuario actualizado." });
    }
    [HttpPut("{id:int}/estado")]
    public async Task<IActionResult> Estado(int id, EstadoUsuarioRequest request)
    {
        var actor = await usuarios.Actual(User);
        if (!actor.PuedeAdministrarUsuarios)
        {
            throw new UnauthorizedAccessException();
        }
        await usuarios.CambiarEstado(id, request.Habilitado, actor.IdUsuario);
        return Ok(new { mensaje = request.Habilitado ? "Usuario activado." : "Usuario deshabilitado." });
    }
    [HttpGet("exportar")]
    public async Task<IActionResult> Exportar()
    {
        if (!(await usuarios.Actual(User)).PuedeAdministrarUsuarios)
        {
            throw new UnauthorizedAccessException();
        }
        return File(await usuarios.Exportar(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "DDCP_usuarios.xlsx");
    }
}

