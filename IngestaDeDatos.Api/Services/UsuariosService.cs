using IngestaDeDatos.Api.Models;
using IngestaDeDatos.Api.Repositories;
using System.Security.Claims;

namespace IngestaDeDatos.Api.Services;

public sealed class UsuariosService(IUsuariosRepository repository)
{
    public Task<UsuarioInfo?> Buscar(string usuario) => repository.Buscar(usuario);
    public Task<UsuarioInfo> Actual(ClaimsPrincipal principal, bool carga = false) => repository.Actual(principal, carga);
    public Task<int> Crear(UsuarioRequest request) => repository.Crear(request);
    public Task Editar(int id, UsuarioRequest request, int actor) => repository.Editar(id, request, actor);
    public Task CambiarEstado(int id, bool habilitado, int actor) => repository.CambiarEstado(id, habilitado, actor);
    public Task Bootstrap() => repository.Bootstrap();
    public Task<List<UsuarioInfo>> Listar() => repository.Listar();
    public async Task<byte[]> Exportar()
    {
        var (lista, entidades) = await repository.DatosExportacion();
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
        return stream.ToArray();
    }
}
