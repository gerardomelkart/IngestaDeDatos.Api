using ClosedXML.Excel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IngestaDeDatos.Api.Readers;

    public static class DdcpArchivoReader
{
    private static readonly string[] Meses = ["", "ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE"];
    public static IXLRow? BuscarEncabezado(IXLWorksheet sheet)
    {
        return sheet.RowsUsed().FirstOrDefault(row => Normalizar(row.Cell(2).GetString()) == "ENTIDAD FEDERATIVA");
    }

    public static bool EsFilaDeDatos(IXLRow row)
    {
        return Regex.IsMatch(row.Cell(1).GetString().Trim(), @"^\d+$") && !string.IsNullOrWhiteSpace(row.Cell(2).GetString());
    }

    public static IXLWorksheet SeleccionarHoja(XLWorkbook workbook, int anio, int mes)
    {
        var candidatas = workbook.Worksheets.Where(sheet => BuscarEncabezado(sheet) is not null || sheet.RowsUsed().Any(EsFilaDeDatos)).ToList();
        if (candidatas.Count == 0)
        {
            throw new ArgumentException("No se encontró una tabla de datos. El archivo debe tener cuatro columnas: número, entidad, dispositivos y personas; puede incluir encabezados o comenzar directamente con los datos.");
        }
        if (candidatas.Count == 1)
        {
            return candidatas[0];
        }

        // En libros con varios meses, el título permite distinguir las tablas.
        // Nunca se utiliza el nombre de la pestaña ni se exige un título para una tabla única.
        var delPeriodo = candidatas.Where(sheet =>
        {
            var encabezado = BuscarEncabezado(sheet);
            var primeraFila = encabezado?.RowNumber() ?? sheet.RowsUsed().First(EsFilaDeDatos).RowNumber();
            var titulo = string.Join(" ", sheet.RowsUsed().Where(row => row.RowNumber() < primeraFila).SelectMany(row => row.CellsUsed()).Select(cell => Normalizar(cell.GetString())));
            return Regex.IsMatch(titulo, $@"\b{anio}\b") && Regex.IsMatch(titulo, $@"\b{Meses[mes]}\b");
        }).ToList();

        if (delPeriodo.Count == 1)
        {
            return delPeriodo[0];
        }
        throw new ArgumentException("El archivo contiene varias tablas y no se puede identificar una sola para cargar. Deje únicamente la tabla que desea reportar. El nombre de la pestaña no importa; se usará el año y mes seleccionado en pantalla.");
    }

    public static XLWorkbook Abrir(Stream stream)
    {
        try { return new XLWorkbook(stream); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { throw new ArgumentException("No fue posible leer el Excel. Verifique que sea .xlsx válido y no esté protegido."); }
    }
    public static bool Numero(IXLCell cell, out decimal numero)
    {
        numero = 0;
        if (cell.HasFormula) return false;
        var texto = cell.GetString().Trim();
        return Regex.IsMatch(texto, @"^\d+$") && decimal.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out numero) && numero >= 0;
    }
    public static string Normalizar(string value) => new string(value.Trim().Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC).ToUpperInvariant();
}
