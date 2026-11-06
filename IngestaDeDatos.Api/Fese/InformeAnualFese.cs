using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace IngestaDeDatos.Api.Fese;

internal static class InformeAnualFese
{
    public static byte[] Generar(IEnumerable<FilaRdsFese> filas, CancellationToken ct)
    {
        using var salida = new MemoryStream();
        using (var documento = SpreadsheetDocument.Create(salida, SpreadsheetDocumentType.Workbook))
        {
            var libro = documento.AddWorkbookPart();
            libro.Workbook = new Workbook();
            var hoja = libro.AddNewPart<WorksheetPart>();
            libro.Workbook.AppendChild(new Sheets()).Append(new Sheet
            {
                Id = libro.GetIdOfPart(hoja), SheetId = 1, Name = "Sheet1"
            });

            using var escritor = OpenXmlWriter.Create(hoja);
            escritor.WriteStartElement(new Worksheet());
            escritor.WriteStartElement(new SheetData());
            string[] cabecera = ["Código", "Año", "Procedencia", "Region", "Estado", "Centro", "Tipo", "Incidente",
                .. Enumerable.Range(1, 12).Select(m => CultureInfo.GetCultureInfo("es-MX").TextInfo.ToTitleCase(ExportacionesFese.Mes(m)))];
            escritor.WriteElement(new Row(cabecera.Select(Texto)));

            var grupos = filas.GroupBy(x => new
            {
                x.Codigo, x.Periodo.Year, x.Procedencia, x.Region,
                Estado = x.Estado.ToUpperInvariant(), x.Centro, x.Tipo, x.Incidente
            }).OrderBy(g => g.Key.Year).ThenBy(g => g.Key.Codigo, StringComparer.Ordinal);
            var contador = 1;
            foreach (var grupo in grupos)
            {
                ct.ThrowIfCancellationRequested();
                if (++contador > 1_048_576)
                    throw new ArgumentException("El corte supera el límite de filas de Excel.");
                var k = grupo.Key;
                var meses = new long[12];
                foreach (var dato in grupo) meses[dato.Periodo.Month - 1] += dato.Total;
                escritor.WriteElement(new Row(
                    Texto(k.Codigo), Numero(k.Year), Texto(k.Procedencia), Texto(k.Region),
                    Texto(k.Estado), Texto(k.Centro), Texto(k.Tipo), Texto(k.Incidente),
                    Numero(meses[0]), Numero(meses[1]), Numero(meses[2]), Numero(meses[3]),
                    Numero(meses[4]), Numero(meses[5]), Numero(meses[6]), Numero(meses[7]),
                    Numero(meses[8]), Numero(meses[9]), Numero(meses[10]), Numero(meses[11])));
            }
            escritor.WriteEndElement();
            escritor.WriteEndElement();
            libro.Workbook.Save();
        }
        return salida.ToArray();
    }

    private static Cell Texto(string valor) => new(new InlineString(new Text(valor)
    {
        Space = SpaceProcessingModeValues.Preserve
    })) { DataType = CellValues.InlineString };

    private static Cell Numero(long valor) => new()
    {
        DataType = CellValues.Number,
        CellValue = new CellValue(valor.ToString(CultureInfo.InvariantCulture))
    };
}
