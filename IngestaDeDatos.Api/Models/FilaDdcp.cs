namespace IngestaDeDatos.Api.Models;

public record FilaDdcp(int IdEntidad, string NombreEntidad, string Dispositivos, int Personas, string? DispositivosAntes, int? PersonasAntes, int? RevisionAntes, bool? HabilitadoAntes);
