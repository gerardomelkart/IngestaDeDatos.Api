namespace IngestaDeDatos.Api.Models;

public sealed class RegistroDdcp
{
    public int IdEntidad { get; set; }
    public string NombreEntidad { get; set; } = "";
    public decimal Dispositivos { get; set; }
    public int Personas { get; set; }
    public int Revision { get; set; }
    public bool Habilitado { get; set; }
    public DateTime Periodo { get; set; }
}
