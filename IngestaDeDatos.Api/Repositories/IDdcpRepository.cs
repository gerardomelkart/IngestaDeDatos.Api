using IngestaDeDatos.Api.Models;

namespace IngestaDeDatos.Api.Repositories;

public interface IDdcpRepository
{
    Task<List<Entidad>> Entidades(int? id);
    Task<List<RegistroDdcp>> Consultar(int anio, int? mes, int? entidad);
    Task CrearVistaPrevia(Guid id, int usuario, DateTime periodo, string nombre, byte[] bytes, string json);
    Task Confirmar(UsuarioInfo usuario, Guid id);
    Task Cancelar(UsuarioInfo usuario, Guid id);
}
