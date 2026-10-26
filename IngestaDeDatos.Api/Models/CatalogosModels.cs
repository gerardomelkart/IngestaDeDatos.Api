namespace IngestaDeDatos.Api.Models;

public sealed class RolCatalogo
{
    public string Clave { get; set; } = "";
    public string Nombre { get; set; } = "";
    public bool PuedeCargar { get; set; }
    public bool PuedeAdministrarUsuarios { get; set; }
    public bool NacionalObligatorio { get; set; }
    public bool Predeterminado { get; set; }
}
public record MesCatalogo(int IdMes, string Nombre);
