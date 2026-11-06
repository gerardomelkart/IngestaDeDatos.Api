using System.Globalization;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace IngestaDeDatos.Api.Fese;

internal static class PlantillaCniedtFese
{
    private static string Clave(string nombre) => LectorFese.Normalizar(nombre) switch
    {
        "ESTADO DE MEXICO" => "MEXICO",
        "COAHUILA DE ZARAGOZA" => "COAHUILA",
        "MICHOACAN DE OCAMPO" => "MICHOACAN",
        "VERACRUZ DE IGNACIO DE LA LLAVE" => "VERACRUZ",
        var clave => clave
    };

    public static byte[] Llenar(MemoryStream archivo, List<DatoFese> datos,
        List<CargaFese> cargas, List<EntidadFese> entidades, DateTime periodo)
    {
        try
        {
            using (var documento = SpreadsheetDocument.Open(archivo, true))
            {
                var libro = documento.WorkbookPart ?? throw new ArgumentException("El archivo no contiene un libro.");
                var hoja = libro.Workbook.Descendants<Sheet>().SingleOrDefault(x => x.Name?.Value == "Llamadas 911");
                if (hoja?.Id?.Value is not string id)
                    throw new ArgumentException("La plantilla debe incluir la pestaña Llamadas 911.");
                var parte = (WorksheetPart)libro.GetPartById(id);
                var filas = parte.Worksheet.GetFirstChild<SheetData>()!.Elements<Row>().ToDictionary(r => r.RowIndex!.Value);
                var compartidos = libro.SharedStringTablePart?.SharedStringTable.Elements<SharedStringItem>().ToArray() ?? [];
                string Leer(Cell? celda) => celda?.DataType?.Value == CellValues.SharedString
                    ? compartidos[int.Parse(celda!.CellValue!.Text, CultureInfo.InvariantCulture)].InnerText
                    : celda?.InnerText ?? "";

                var encabezado = filas.GetValueOrDefault(1u)?.Elements<Cell>().FirstOrDefault(c => c.CellReference?.Value == "A1");
                if (!Leer(encabezado).Contains(periodo.Year.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
                    throw new ArgumentException("El año de la plantilla no coincide con el corte.");
                var columna = (char)('A' + periodo.Month);
                var mes = filas.GetValueOrDefault(2u)?.Elements<Cell>().SingleOrDefault(c => c.CellReference?.Value == $"{columna}2");
                if (Clave(Leer(mes)) != Clave(ExportacionesFese.Mes(periodo.Month)))
                    throw new ArgumentException("La columna del mes no coincide con el formato CNIEDT.");

                var catalogo = entidades.ToDictionary(x => Clave(x.Nombre), x => x.IdEntidad);
                var aceptadas = cargas.Where(x => x.Estado == "ACEPTADA" && x.Periodo.Month == periodo.Month)
                    .Select(x => x.IdEntidad).ToHashSet();
                if (aceptadas.Count == 0) throw new ArgumentException("El mes seleccionado no tiene entregas aceptadas.");
                var totales = datos.Where(x => x.Procedente && x.Periodo.Month == periodo.Month)
                    .GroupBy(x => x.IdEntidad).ToDictionary(g => g.Key, g => g.Sum(x => x.Total));
                var vistas = new HashSet<int>();
                for (uint indice = 3; indice <= 34; indice++)
                {
                    if (!filas.TryGetValue(indice, out var fila)
                        || !catalogo.TryGetValue(Clave(Leer(fila.Elements<Cell>().FirstOrDefault(c => c.CellReference?.Value == $"A{indice}"))), out var entidad)
                        || !vistas.Add(entidad))
                        throw new ArgumentException($"Revise la entidad de la fila {indice} en Llamadas 911.");
                    if (!aceptadas.Contains(entidad)) continue;
                    var referencia = $"{columna}{indice}";
                    var celdas = fila.Elements<Cell>();
                    var destino = celdas.FirstOrDefault(c => c.CellReference?.Value == referencia);
                    if (destino is null)
                    {
                        destino = new Cell { CellReference = referencia };
                        fila.InsertBefore(destino, celdas.FirstOrDefault(c => c.CellReference?.Value is string r && r.Length == referencia.Length && r[0] > columna));
                    }
                    destino.CellFormula = null;
                    destino.InlineString = null;
                    destino.DataType = CellValues.Number;
                    destino.CellValue = new CellValue(totales.GetValueOrDefault(entidad).ToString(CultureInfo.InvariantCulture));
                }
                parte.Worksheet.Save();
            }
            return archivo.ToArray();
        }
        catch (Exception ex) when (ex is OpenXmlPackageException or IOException or System.Xml.XmlException)
        {
            throw new ArgumentException("No se pudo abrir la plantilla CNIEDT.", ex);
        }
    }
}
