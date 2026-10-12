namespace IngestaDeDatos.Api.Models;

public record PreviaDdcp(Guid IdCarga, int Anio, int Mes, List<FilaDdcp> Filas, List<string> Advertencias);
