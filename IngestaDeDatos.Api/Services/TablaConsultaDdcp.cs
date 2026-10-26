using System.Globalization;
using IngestaDeDatos.Api.Models;
using static IngestaDeDatos.Api.Readers.DdcpArchivoReader;

namespace IngestaDeDatos.Api.Services;

public static class TablaConsultaDdcp
{
    public static List<RegistroDdcp> Aplicar(IEnumerable<RegistroDdcp> filas, IReadOnlyCollection<MesCatalogo> meses, FiltrosTablaDdcp filtros)
    {
        bool Coincide(string valor, string? texto) => Normalizar(valor).Contains(Normalizar(texto ?? ""), StringComparison.Ordinal);
        var visibles = filas.Where(fila =>
            Coincide(fila.Periodo.Year.ToString(CultureInfo.InvariantCulture), filtros.FiltroAnio) &&
            Coincide(meses.Single(x => x.IdMes == fila.Periodo.Month).Nombre, filtros.FiltroMes) &&
            Coincide(fila.NombreEntidad, filtros.FiltroEntidad) &&
            Coincide(fila.Dispositivos.ToString("0", CultureInfo.InvariantCulture), filtros.FiltroDispositivos) &&
            Coincide(fila.Personas.ToString(CultureInfo.InvariantCulture), filtros.FiltroPersonas));
        IOrderedEnumerable<RegistroDdcp> Ordenar<T>(Func<RegistroDdcp, T> valor, IComparer<T>? comparador = null) =>
            filtros.Ascendente ? visibles.OrderBy(valor, comparador) : visibles.OrderByDescending(valor, comparador);
        var ordenadas = filtros.Orden switch
        {
            "mes" => Ordenar(x => x.Periodo.Month),
            "entidad" => Ordenar(x => x.NombreEntidad, StringComparer.Create(CultureInfo.GetCultureInfo("es-MX"), true)),
            "dispositivos" => Ordenar(x => x.Dispositivos),
            "personas" => Ordenar(x => x.Personas),
            _ => Ordenar(x => x.Periodo.Year)
        };
        return ordenadas.ThenBy(x => x.Periodo).ThenBy(x => x.IdEntidad).ToList();
    }
}
