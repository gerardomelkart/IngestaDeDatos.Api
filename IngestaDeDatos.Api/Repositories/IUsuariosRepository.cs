using IngestaDeDatos.Api.Models;
using System.Security.Claims;

namespace IngestaDeDatos.Api.Repositories;

public interface IUsuariosRepository
{
    Task<UsuarioInfo?> Buscar(string usuario);
    Task<UsuarioInfo> Actual(ClaimsPrincipal principal, bool carga = false);
    Task<int> Crear(UsuarioRequest request);
    Task Editar(int id, UsuarioRequest request, int actor);
    Task CambiarEstado(int id, bool habilitado, int actor);
    Task Bootstrap();
    Task<List<UsuarioInfo>> Listar();
    Task<(List<UsuarioInfo> Usuarios, Dictionary<int, string> Entidades)> DatosExportacion();
}
