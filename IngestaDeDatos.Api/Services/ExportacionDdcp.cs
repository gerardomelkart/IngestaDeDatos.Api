using ClosedXML.Excel;
using IngestaDeDatos.Api.Models;
using System.Globalization;

namespace IngestaDeDatos.Api.Services;

public static class ExportacionDdcp
{
    private static readonly string[] Meses = ["Enero", "Febrero", "Marzo", "Abril", "Mayo", "Junio", "Julio", "Agosto", "Septiembre", "Octubre", "Noviembre", "Diciembre"];

    public static byte[] Generar(IReadOnlyCollection<RegistroDdcp> filas, int anio, int? mes, int? entidad)
    {
        using var libro = new XLWorkbook();
        var hoja = libro.AddWorksheet("Consulta DDCP");
        hoja.Cell(1, 1).Value = "Dispositivos decomisados centros penitenciarios";
        hoja.Range(1, 1, 1, 5).Merge();
        var periodo = mes.HasValue ? $"Año: {anio} · Mes: {Meses[mes.Value - 1]}" : $"Año: {anio} · Todos los meses";
        var alcance = entidad.HasValue ? $"Entidad: {filas.FirstOrDefault()?.NombreEntidad ?? entidad.Value.ToString(CultureInfo.InvariantCulture)}" : "Todas las entidades";
        hoja.Cell(2, 1).Value = $"{periodo} · {alcance}";
        hoja.Range(2, 1, 2, 5).Merge();
        hoja.Cell(3, 1).Value = "Datos confirmados. Las cantidades de más de 15 dígitos se conservan como texto para evitar pérdida de precisión.";
        hoja.Range(3, 1, 3, 5).Merge();
        string[] columnas = ["Año", "Mes", "Entidad", "Dispositivos decomisados", "Personas puestas a disposición"];
        for (var columna = 0; columna < columnas.Length; columna++)
        {
            hoja.Cell(4, columna + 1).Value = columnas[columna];
        }
        var numeroFila = 5;
        foreach (var fila in filas)
        {
            hoja.Cell(numeroFila, 1).Value = fila.Periodo.Year;
            hoja.Cell(numeroFila, 2).Value = Meses[fila.Periodo.Month - 1];
            hoja.Cell(numeroFila, 3).Value = fila.NombreEntidad;
            Cantidad(hoja.Cell(numeroFila, 4), fila.Dispositivos);
            Cantidad(hoja.Cell(numeroFila, 5), fila.Personas);
            numeroFila++;
        }
        hoja.Cell(numeroFila, 3).Value = "Total";
        Cantidad(hoja.Cell(numeroFila, 4), filas.Sum(fila => fila.Dispositivos));
        Cantidad(hoja.Cell(numeroFila, 5), filas.Sum(fila => (long)fila.Personas));
        hoja.Range(numeroFila, 1, numeroFila, 5).Style.Font.Bold = true;
        hoja.Range(numeroFila, 1, numeroFila, 5).Style.Fill.BackgroundColor = XLColor.FromHtml("#F3E9EC");
        foreach (var rango in new[] { hoja.Range(1, 1, 1, 5), hoja.Range(4, 1, 4, 5) })
        {
            rango.Style.Font.Bold = true;
            rango.Style.Font.FontColor = XLColor.White;
            rango.Style.Fill.BackgroundColor = XLColor.FromHtml("#691C32");
        }
        hoja.Column(1).Width = 10;
        hoja.Column(2).Width = 16;
        hoja.Column(3).Width = 32;
        hoja.Columns(4, 5).Width = 27;
        hoja.Range(1, 1, numeroFila, 5).Style.Alignment.WrapText = true;
        hoja.Row(1).Height = 30;
        hoja.Row(2).Height = 28;
        hoja.Row(3).Height = 32;
        hoja.Row(4).Height = 34;
        hoja.SheetView.FreezeRows(4);
        if (filas.Count > 0)
        {
            hoja.Range(4, 1, numeroFila - 1, 5).SetAutoFilter();
        }
        using var stream = new MemoryStream();
        libro.SaveAs(stream);
        return stream.ToArray();
    }
    private static void Cantidad(IXLCell celda, decimal valor)
    {
        if (valor > 999999999999999m)
        {
            celda.Style.NumberFormat.Format = "@";
            celda.Value = valor.ToString("0", CultureInfo.InvariantCulture);
        }
        else
        {
            celda.Value = (double)valor;
            celda.Style.NumberFormat.Format = "#,##0";
        }
    }
}
