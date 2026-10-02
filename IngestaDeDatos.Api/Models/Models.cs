namespace IngestaDeDatos.Api.Models;

public record LoginRequest(string Usuario, string Password);
public record UsuarioRequest(string Usuario, string Nombre, string Password, string Rol, bool EsNacional, int? IdEntidad);
public record EstadoUsuarioRequest(bool Habilitado);
public record Entidad(int IdEntidad, string NombreEntidad, string? NombreCorto);
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
public record FilaDdcp(int IdEntidad, string NombreEntidad, string Dispositivos, int Personas, string? DispositivosAntes, int? PersonasAntes, int? RevisionAntes, bool? HabilitadoAntes);
public record PreviaDdcp(Guid IdCarga, int Anio, int Mes, List<FilaDdcp> Filas, List<string> Advertencias);
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
public sealed class CargaInfo
{
    public Guid IdCarga { get; set; }
    public int IdUsuario { get; set; }
    public DateTime Periodo { get; set; }
    public string Estado { get; set; } = "";
    public string FilasJson { get; set; } = "";
}

