namespace IngestaDeDatos.Api.Models;

public sealed class CargaInfo
{
    public Guid IdCarga { get; set; }
    public int IdUsuario { get; set; }
    public DateTime Periodo { get; set; }
    public string Estado { get; set; } = "";
    public string FilasJson { get; set; } = "";
}
