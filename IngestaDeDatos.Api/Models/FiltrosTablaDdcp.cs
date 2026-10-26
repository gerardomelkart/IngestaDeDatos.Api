namespace IngestaDeDatos.Api.Models;

public sealed class FiltrosTablaDdcp
{
    public string? FiltroAnio { get; set; }
    public string? FiltroMes { get; set; }
    public string? FiltroEntidad { get; set; }
    public string? FiltroDispositivos { get; set; }
    public string? FiltroPersonas { get; set; }
    public string Orden { get; set; } = "anio";
    public bool Ascendente { get; set; } = true;
}
