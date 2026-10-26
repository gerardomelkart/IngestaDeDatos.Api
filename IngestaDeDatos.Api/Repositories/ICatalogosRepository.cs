using IngestaDeDatos.Api.Models;

namespace IngestaDeDatos.Api.Repositories;

public interface ICatalogosRepository
{
    string Modulo { get; }
    Task<List<RolCatalogo>> Roles();
    Task<List<MesCatalogo>> Meses();
    Task<string> NombreModulo();
}
