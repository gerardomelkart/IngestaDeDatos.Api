using System.ComponentModel.DataAnnotations;

namespace IngestaDeDatos.Api.Configuration;

public sealed class ReglasDdcpOptions
{
    [Range(1, 9999)] public int AnioMinimo { get; set; }
    [Range(1, 9999)] public int AnioMaximo { get; set; }
    [Range(1, int.MaxValue)] public long MaxArchivoBytes { get; set; }
    [Range(1, int.MaxValue)] public long MargenMultipartBytes { get; set; }
    public decimal MaxDispositivos { get; set; }
    [Range(1, int.MaxValue)] public int MaxPersonas { get; set; }
    [Range(1, 80)] public int UsuarioMaximo { get; set; }
    [Range(1, 160)] public int NombreMaximo { get; set; }
    [Range(1, 72)] public int PasswordMinimo { get; set; }
    [Range(1, 72)] public int PasswordMaximoBytes { get; set; }
    [Range(4, 31)] public int BCryptWorkFactor { get; set; }
    [Range(1, int.MaxValue)] public int FilasPagina { get; set; }
    public bool EsValida() => AnioMinimo <= AnioMaximo && PasswordMinimo <= PasswordMaximoBytes
        && MaxDispositivos > 0 && MaxDispositivos <= 999999999999999999m;
}
