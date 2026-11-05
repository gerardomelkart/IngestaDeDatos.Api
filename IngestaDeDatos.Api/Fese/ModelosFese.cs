namespace IngestaDeDatos.Api.Fese;

public sealed class IncidenteFese
{
    public int Codigo { get; set; }
    public string Nombre { get; set; } = "";
    public string Categoria { get; set; } = "";
    public bool Procedente { get; set; }
}

public sealed record FilaFese(string Centro, int Codigo, long Total);
public sealed record LecturaFese(List<FilaFese> Filas, List<string> Errores, List<string> Advertencias);
public sealed record EntidadFese(int IdEntidad, string Nombre);
public sealed class CargaFese
{
    public Guid IdCarga { get; set; }
    public int IdEntidad { get; set; }
    public string Entidad { get; set; } = "";
    public DateTime Periodo { get; set; }
    public int IdUsuario { get; set; }
    public string Usuario { get; set; } = "";
    public string Estado { get; set; } = "";
    public string Tipo { get; set; } = "";
    public Guid? IdCargaBase { get; set; }
    public string NombreArchivo { get; set; } = "";
    public DateTimeOffset FechaCreacion { get; set; }
    public DateTimeOffset? FechaAceptacion { get; set; }
    public string Advertencias { get; set; } = "[]";
    public string ComparativoJson { get; set; } = "[]";
}
public sealed class DatoFese
{
    public int IdEntidad { get; set; }
    public string Entidad { get; set; } = "";
    public DateTime Periodo { get; set; }
    public string Centro { get; set; } = "";
    public int Codigo { get; set; }
    public string Incidente { get; set; } = "";
    public string Categoria { get; set; } = "";
    public bool Procedente { get; set; }
    public long Total { get; set; }
}
public sealed record DiferenciaFese(string Centro, int Codigo, long? Anterior, long? Nuevo);

public sealed class CuentaFese
{
    public int IdUsuario { get; set; }
    public string Usuario { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string Rol { get; set; } = "";
    public bool EsNacional { get; set; }
    public int? IdEntidad { get; set; }
    public bool Habilitado { get; set; }
    public bool AccesoFese { get; set; }
    public bool HabilitaCarga { get; set; }
    public bool HabilitaActualizacion { get; set; }
    public long VersionPermisos { get; set; }
}
