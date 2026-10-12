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
        var lista = await db.QueryAsync<UsuarioInfo>("SELECT ID_USUARIO IdUsuario, USUARIO Usuario, NOMBRE Nombre, ROL Rol, ES_NACIONAL EsNacional, ID_ENTIDAD IdEntidad, HABILITADO Habilitado FROM dbo.ING_USUARIO ORDER BY USUARIO;");
        return Ok(lista.Select(x => new { x.IdUsuario, x.Usuario, x.Nombre, x.Rol, x.EsNacional, x.IdEntidad, x.Habilitado }));
    }
    [HttpPost]
    public async Task<IActionResult> Crear(UsuarioRequest request)
    {
        if ((await usuarios.Actual(User)).Rol != "SUPER_USUARIO") throw new UnauthorizedAccessException();
        return Ok(new { idUsuario = await usuarios.Crear(request) });
    }
    [HttpPut("{id:int}")]
    public async Task<IActionResult> Editar(int id, UsuarioRequest request)
    {
        var actor = await usuarios.Actual(User);
        if (actor.Rol != "SUPER_USUARIO")
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
        if (actor.Rol != "SUPER_USUARIO")
        {
            throw new UnauthorizedAccessException();
        }
        await usuarios.CambiarEstado(id, request.Habilitado, actor.IdUsuario);
        return Ok(new { mensaje = request.Habilitado ? "Usuario activado." : "Usuario deshabilitado." });
    }
    [HttpGet("exportar")]
    public async Task<IActionResult> Exportar()
    {
        if ((await usuarios.Actual(User)).Rol != "SUPER_USUARIO")
        {
            throw new UnauthorizedAccessException();
        }
        await using var db = factory.Auth();
        var lista = await db.QueryAsync<UsuarioInfo>("SELECT ID_USUARIO IdUsuario, USUARIO Usuario, NOMBRE Nombre, ROL Rol, ES_NACIONAL EsNacional, ID_ENTIDAD IdEntidad, HABILITADO Habilitado FROM dbo.ING_USUARIO ORDER BY USUARIO;");
        var entidades = (await db.QueryAsync<Entidad>("SELECT ID_ENTIDAD IdEntidad, NOMBRE_ENTIDAD NombreEntidad, NOMBRE_CORTO NombreCorto FROM dbo.CAT_ENTIDAD;")).ToDictionary(x => x.IdEntidad, x => x.NombreEntidad);
        using var workbook = new ClosedXML.Excel.XLWorkbook();
        var sheet = workbook.AddWorksheet("Usuarios DDCP");
        string[] encabezados = ["Nombre", "Usuario", "Rol", "Alcance", "Entidad", "Estado"];
        for (var i = 0; i < encabezados.Length; i++)
        {
            sheet.Cell(1, i + 1).Value = encabezados[i];
        }
        var fila = 2;
        foreach (var usuario in lista)
        {
            sheet.Cell(fila, 1).Value = usuario.Nombre;
            sheet.Cell(fila, 2).Value = usuario.Usuario;
            sheet.Cell(fila, 3).Value = usuario.Rol;
            sheet.Cell(fila, 4).Value = usuario.EsNacional ? "Nacional" : "Por entidad";
            sheet.Cell(fila, 5).Value = usuario.EsNacional ? "Todas" : entidades.GetValueOrDefault(usuario.IdEntidad ?? 0, "Entidad asignada");
            sheet.Cell(fila, 6).Value = usuario.Habilitado ? "Activo" : "Inactivo";
            fila++;
        }
        sheet.Range(1, 1, 1, 6).Style.Font.Bold = true;
        sheet.Columns().AdjustToContents();
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "DDCP_usuarios.xlsx");
    }
}

