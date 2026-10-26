using IngestaDeDatos.Api.Configuration;

namespace IngestaDeDatos.Api.Validators;

public static class DdcpPeriodoValidator
{
    public static void Validar(int anio, int? mes, ReglasDdcpOptions reglas)
    {
        if (anio < reglas.AnioMinimo || anio > reglas.AnioMaximo || mes.HasValue && (mes < 1 || mes > 12)) throw new ArgumentException($"Periodo inválido: año {reglas.AnioMinimo} a {reglas.AnioMaximo} y un mes del calendario.");
    }
}

