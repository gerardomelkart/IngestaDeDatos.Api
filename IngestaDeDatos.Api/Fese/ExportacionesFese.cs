using ClosedXML.Excel;
using System.Globalization;

namespace IngestaDeDatos.Api.Fese;

public static class ExportacionesFese
{
    public static byte[] Excel(IEnumerable<DatoFese> datos, bool anual)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet(anual ? "Reporte anual" : "Consulta FESE");
        string[] columnas = anual
            ? ["Entidad", "Centro", "Código", "Incidente", "Categoría", "Procedencia", .. Enumerable.Range(1, 12).Select(Mes)]
            : ["Entidad", "Periodo", "Centro", "Código", "Incidente", "Categoría", "Procedencia", "Total"];
        for (var i = 0; i < columnas.Length; i++) hoja.Cell(1, i + 1).Value = columnas[i];
        var fila = 2;
        if (anual)
        {
            foreach (var grupo in datos.GroupBy(x => new { x.Entidad, x.Centro, x.Codigo, x.Incidente, x.Categoria, x.Procedente }))
            {
                var k = grupo.Key;
                hoja.Cell(fila, 1).Value = k.Entidad;
                hoja.Cell(fila, 2).Value = k.Centro;
                hoja.Cell(fila, 3).Value = k.Codigo;
                hoja.Cell(fila, 4).Value = k.Incidente;
                hoja.Cell(fila, 5).Value = k.Categoria;
                hoja.Cell(fila, 6).Value = k.Procedente ? "Procedentes" : "Improcedentes";
                foreach (var mes in grupo.GroupBy(x => x.Periodo.Month)) hoja.Cell(fila, mes.Key + 6).Value = mes.Sum(x => x.Total);
                fila++;
            }
        }
        else
        {
            foreach (var d in datos)
            {
                hoja.Cell(fila, 1).Value = d.Entidad;
                hoja.Cell(fila, 2).Value = d.Periodo.ToString("yyyy-MM");
                hoja.Cell(fila, 3).Value = d.Centro;
                hoja.Cell(fila, 4).Value = d.Codigo;
                hoja.Cell(fila, 5).Value = d.Incidente;
                hoja.Cell(fila, 6).Value = d.Categoria;
                hoja.Cell(fila, 7).Value = d.Procedente ? "Procedentes" : "Improcedentes";
                hoja.Cell(fila, 8).Value = d.Total;
                fila++;
            }
        }
        Estilo(hoja, columnas.Length);
        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] Diferencias(List<FilaFese> anteriores, List<FilaFese> nuevas)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet("Diferencias");
        string[] titulos = ["Centro", "Código", "Cantidad anterior", "Cantidad nueva", "Cambio"];
        for (var i = 0; i < titulos.Length; i++) hoja.Cell(1, i + 1).Value = titulos[i];
        var a = anteriores.ToDictionary(x => (LectorFese.Normalizar(x.Centro), x.Codigo));
        var n = nuevas.ToDictionary(x => (LectorFese.Normalizar(x.Centro), x.Codigo));
        var fila = 2;
        foreach (var clave in a.Keys.Union(n.Keys).OrderBy(x => x.Item1).ThenBy(x => x.Codigo))
        {
            var viejo = a.GetValueOrDefault(clave);
            var nuevo = n.GetValueOrDefault(clave);
            if (viejo?.Total == nuevo?.Total) continue;
            hoja.Cell(fila, 1).Value = nuevo?.Centro ?? viejo!.Centro;
            hoja.Cell(fila, 2).Value = clave.Codigo;
            if (viejo is not null) hoja.Cell(fila, 3).Value = viejo.Total;
            if (nuevo is not null) hoja.Cell(fila, 4).Value = nuevo.Total;
            hoja.Cell(fila, 5).Value = viejo is null ? "Nuevo" : nuevo is null ? "Eliminado" : "Modificado";
            fila++;
        }
        Estilo(hoja, 5);
        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        return stream.ToArray();
    }

    public static byte[] Cniedt(List<DatoFese> datos, List<CargaFese> cargas, List<EntidadFese> entidades, int anio)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet("Llamadas 911");
        hoja.Cell(1, 1).Value = $"Llamadas procedentes al servicio 9-1-1 · {anio}";
        hoja.Cell(2, 1).Value = "Entidad";
        for (var mes = 1; mes <= 12; mes++) hoja.Cell(2, mes + 1).Value = Mes(mes);
        var vigentes = cargas.Where(x => x.Estado == "ACEPTADA").Select(x => (x.IdEntidad, x.Periodo.Month)).ToHashSet();
        for (var i = 0; i < entidades.Count; i++)
        {
            var entidad = entidades[i];
            hoja.Cell(i + 3, 1).Value = entidad.Nombre;
            for (var mes = 1; mes <= 12; mes++)
            {
                if (!vigentes.Contains((entidad.IdEntidad, mes))) continue;
                hoja.Cell(i + 3, mes + 1).Value = datos.Where(x => x.IdEntidad == entidad.IdEntidad && x.Periodo.Month == mes && x.Procedente).Sum(x => x.Total);
            }
        }
        var filaNacional = entidades.Count + 3;
        hoja.Cell(filaNacional, 1).Value = "Nacional";
        var cobertura = libro.AddWorksheet("Cobertura");
        cobertura.Cell(1, 1).Value = "Mes";
        cobertura.Cell(1, 2).Value = "Entidades recibidas";
        cobertura.Cell(1, 3).Value = "Estado";
        for (var mes = 1; mes <= 12; mes++)
        {
            var recibidas = entidades.Count(e => vigentes.Contains((e.IdEntidad, mes)));
            cobertura.Cell(mes + 1, 1).Value = Mes(mes);
            cobertura.Cell(mes + 1, 2).Value = recibidas;
            cobertura.Cell(mes + 1, 3).Value = recibidas == 32 ? "Completo" : "Incompleto";
            // Un nacional parcial no se presenta como un nacional completo.
            if (recibidas == 32)
                hoja.Cell(filaNacional, mes + 1).Value = datos.Where(x => x.Periodo.Month == mes && x.Procedente).Sum(x => x.Total);
        }
        hoja.Cell(filaNacional + 2, 1).Value = "Celdas vacías: sin entrega aceptada. Nacional disponible con las 32 entidades. Consulte Cobertura.";
        Estilo(hoja, 13, 2);
        Estilo(cobertura, 3);
        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        return stream.ToArray();
    }
    public static string Mes(int mes) => CultureInfo.GetCultureInfo("es-MX").DateTimeFormat.GetMonthName(mes);
    private static void Estilo(IXLWorksheet hoja, int columnas, int cabecera = 1)
    {
        hoja.Range(cabecera, 1, cabecera, columnas).Style.Fill.BackgroundColor = XLColor.FromHtml("#611232");
        hoja.Range(cabecera, 1, cabecera, columnas).Style.Font.FontColor = XLColor.White;
        hoja.Range(cabecera, 1, cabecera, columnas).Style.Font.Bold = true;
        hoja.SheetView.FreezeRows(cabecera);
        hoja.Columns().AdjustToContents(4, 55);
        hoja.RangeUsed()?.SetAutoFilter();
    }

    public static byte[] Acuse(CargaFese carga, List<FilaFese> filas, List<DatoFese> anteriores, List<IncidenteFese> catalogo, bool aceptado, DateTimeOffset? fechaEmision = null)
    {
        return AcuseFese.Crear(carga, filas, anteriores, catalogo, aceptado,
            fechaEmision ?? carga.FechaAceptacion ?? RepositorioFese.Ahora());
    }
}
