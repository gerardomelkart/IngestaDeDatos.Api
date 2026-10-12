namespace IngestaDeDatos.Api.Models;

public sealed class UsuarioInfo
{
    public int IdUsuario { get; set; }
    public string Usuario { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Rol { get; set; } = "";
    public bool EsNacional { get; set; }
    public int? IdEntidad { get; set; }
    public bool Habilitado { get; set; }
}
