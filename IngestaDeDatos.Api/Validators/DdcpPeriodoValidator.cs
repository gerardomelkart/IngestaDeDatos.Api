namespace IngestaDeDatos.Api.Validators;

public static class DdcpPeriodoValidator
{
    public static void Validar(int anio, int? mes)
    {
        if (anio < 2000 || anio > 2100 || mes.HasValue && (mes < 1 || mes > 12)) throw new ArgumentException("Periodo inválido: año 2000 a 2100 y mes 1 a 12.");
    }
}
