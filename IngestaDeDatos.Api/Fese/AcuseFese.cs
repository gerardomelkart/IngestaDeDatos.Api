using System.Globalization;
using System.Text;
using System.Text.Json;

namespace IngestaDeDatos.Api.Fese;

// Comprobante institucional. Los datos y el comparativo pertenecen a la carga,
// no a una consulta mutable del último periodo disponible.
internal sealed class AcuseFese
{
    private static readonly CultureInfo Cultura = CultureInfo.GetCultureInfo("es-MX");
    private readonly List<StringBuilder> paginas = [];
    private StringBuilder contenido = new();
    private double y;
    private const double Izquierda = 54;
    private const double Derecha = 558;
    private const string Azul = "0 0 1";

    public static byte[] Crear(CargaFese carga, List<FilaFese> filas, List<DatoFese> anteriores, List<IncidenteFese> catalogo, bool aceptado, DateTimeOffset fecha)
    {
        var doc = new AcuseFese();
        doc.NuevaPagina();
        doc.Texto($"{fecha.Day} de {ExportacionesFese.Mes(fecha.Month)} de {fecha.Year}, CDMX", Derecha, 698, 10, derecha: true);
        doc.y = 648;
        var periodo = $"{ExportacionesFese.Mes(carga.Periodo.Month)} de {carga.Periodo.Year}";
        var texto = aceptado
            ? $"El Centro Nacional de Información y Desarrollo Tecnológico emite el presente documento que certifica la entrega de información por parte de la entidad federativa de {carga.Entidad}, para el reporte estadístico mensual de la cantidad de llamadas recibidas en el servicio de atención de emergencias 9-1-1 correspondiente al mes de {periodo}."
            : $"VISTA PREVIA - PENDIENTE DE ACEPTACIÓN. Información presentada por la entidad federativa de {carga.Entidad} para el reporte estadístico mensual de llamadas recibidas en el servicio de atención de emergencias 9-1-1 correspondiente al mes de {periodo}. ";
        doc.Parrafo(texto, 11, 15);
        if (!aceptado) doc.Parrafo("Este documento no acredita una entrega aceptada.", 10, 14);
        doc.y -= 30;
        doc.Parrafo("Comparativo contra el mes anterior:", 11, 15);
        doc.y -= 14;
        var mesAnterior = carga.Periodo.AddMonths(-1);
        doc.Linea(doc.y + 15);
        doc.Texto("Tipos generales de incidentes", Izquierda + 5, doc.y, 10, negrita: true);
        doc.Texto(Cultura.TextInfo.ToTitleCase(ExportacionesFese.Mes(carga.Periodo.Month)), 397, doc.y, 10, true, true);
        doc.Texto(Cultura.TextInfo.ToTitleCase(ExportacionesFese.Mes(mesAnterior.Month)), 474, doc.y, 10, true, true);
        doc.Texto("Variación %", Derecha - 5, doc.y, 10, true, true);
        doc.Linea(doc.y - 7);
        doc.y -= 25;
        var mapa = catalogo.ToDictionary(i => i.Codigo);
        var categorias = catalogo.GroupBy(i => i.Categoria)
            .OrderBy(g => g.Min(i => i.Codigo)).Select(g => g.Key).ToList();
        var indice = 0;
        foreach (var categoria in categorias)
        {
            var actual = filas.Where(f => mapa[f.Codigo].Categoria == categoria).Sum(f => f.Total);
            var previo = anteriores.Where(d => d.Categoria == categoria).Sum(d => d.Total);
            doc.Fila($"{++indice}.- {categoria}", actual, previo, anteriores.Count > 0, indice % 2 == 1);
        }
        doc.Linea(doc.y + 14);
        doc.Fila("TOTALES", filas.Sum(f => f.Total), anteriores.Sum(d => d.Total), anteriores.Count > 0, false, true);
        doc.Linea(doc.y + 13);
        doc.Linea(doc.y + 10);
        doc.y -= 17;
        doc.Parrafo("Nota metodológica: Las variaciones porcentuales iguales o superiores al 10% en valor absoluto se resaltan en color azul para facilitar la identificación de los cambios más significativos respecto del mes anterior.", 8, 11);
        if (anteriores.Count == 0 || anteriores.Sum(d => d.Total) == 0)
            doc.Parrafo("N/D: no se cuenta con una entrega del mes anterior o el denominador es cero. No representa una variación de cero.", 8, 11);
        doc.y -= 22;
        var advertencias = JsonSerializer.Deserialize<List<string>>(carga.Advertencias) ?? [];
        doc.Parrafo("Observaciones: " + (advertencias.Count == 0 ? "Sin observaciones." : string.Join(" ", advertencias)), 10, 14);
        doc.y -= 17;
        doc.Parrafo($"Folio: {carga.IdCarga} | {(carga.Tipo == "ACTUALIZACION" ? "Actualización" : "Entrega inicial")}", 8, 11);
        doc.Parrafo($"Usuario de carga: {carga.Usuario}", 8, 11);
        return doc.Generar();
    }

    private void Fila(string nombre, long actual, long previo, bool hayAnterior, bool sombrear, bool total = false)
    {
        if (sombrear) contenido.Append(FormattableString.Invariant($"0.94 0.94 0.94 rg 54 {y - 5} 504 18 re f\n"));
        Texto(nombre, Izquierda + 5, y, 10, negrita: total);
        Texto(actual.ToString("N0", Cultura), 397, y, 10, true, total);
        Texto(hayAnterior ? previo.ToString("N0", Cultura) : "N/D", 474, y, 10, true, total);
        decimal? variacion = hayAnterior && previo != 0 ? ((decimal)actual - previo) * 100 / previo : null;
        Texto(variacion?.ToString("0.00", Cultura) ?? "N/D", Derecha - 5, y, 10, true, total,
            variacion.HasValue && Math.Abs(variacion.Value) >= 10 ? Azul : "0 0 0");
        y -= 18;
    }

    private void NuevaPagina()
    {
        if (contenido.Length > 0) paginas.Add(contenido);
        contenido = new StringBuilder("q 308 0 0 49.49 250 738 cm /Logo Do Q\n");
        Texto("Comprobante", Izquierda, 735, 12);
        Linea(728);
        y = 695;
    }
    private void Linea(double alto)
    {
        contenido.Append(FormattableString.Invariant($"0 0 0 RG 0.6 w 54 {alto} m 558 {alto} l S\n"));
    }
    private static double Ancho(string texto, double tamano)
    {
        // Métricas Helvetica estándar, compartidas por el cálculo de ajuste y PDF.
        return texto.Sum(c => Anchos[Math.Min((int)c, 255)]) * tamano / 1000;
    }
    private void Texto(string texto, double x, double alto, double tamano, bool derecha = false, bool negrita = false, string color = "0 0 0")
    {
        if (derecha) x -= Ancho(texto, tamano);
        var seguro = texto.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)").Replace("\r", " ").Replace("\n", " ");
        contenido.Append(FormattableString.Invariant($"{color} rg BT /{(negrita ? "F2" : "F1")} {tamano} Tf {x:0.##} {alto:0.##} Td ({seguro}) Tj ET\n"));
    }
    private void Parrafo(string texto, double tamano, double interlineado)
    {
        var linea = "";
        foreach (var palabra in texto.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            var candidata = linea.Length == 0 ? palabra : linea + " " + palabra;
            if (Ancho(candidata, tamano) > Derecha - Izquierda && linea.Length > 0)
            {
                EscribirLinea(linea, tamano, interlineado);
                linea = palabra;
            }
            else linea = candidata;
        }
        if (linea.Length > 0) EscribirLinea(linea, tamano, interlineado);
    }
    private void EscribirLinea(string linea, double tamano, double interlineado)
    {
        if (y < 64) NuevaPagina();
        Texto(linea, Izquierda, y, tamano);
        y -= interlineado;
    }
    private byte[] Generar()
    {
        paginas.Add(contenido);
        using var recurso = typeof(AcuseFese).Assembly.GetManifestResourceStream("IngestaDeDatos.Api.Fese.Recursos.encabezado.base64")
            ?? throw new InvalidOperationException("Falta el encabezado institucional del comprobante.");
        using var lector = new StreamReader(recurso);
        var logo = Convert.FromBase64String(lector.ReadToEnd());
        var encoding = Encoding.Latin1;
        var objetos = new List<byte[]>();
        void Agregar(string objeto) => objetos.Add(encoding.GetBytes(objeto));
        Agregar("<< /Type /Catalog /Pages 2 0 R >>");
        Agregar($"<< /Type /Pages /Kids [{string.Join(" ", Enumerable.Range(0, paginas.Count).Select(i => $"{6 + i * 2} 0 R"))}] /Count {paginas.Count} >>");
        Agregar("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>");
        Agregar("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>");
        using (var imagen = new MemoryStream())
        {
            imagen.Write(encoding.GetBytes($"<< /Type /XObject /Subtype /Image /Width 1500 /Height 241 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {logo.Length} >>\nstream\n"));
            imagen.Write(logo);
            imagen.Write(encoding.GetBytes("\nendstream"));
            objetos.Add(imagen.ToArray());
        }
        for (var i = 0; i < paginas.Count; i++)
        {
            Agregar($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 3 0 R /F2 4 0 R >> /XObject << /Logo 5 0 R >> >> /Contents {7 + i * 2} 0 R >>");
            var cuerpo = paginas[i].ToString() + $"0 0 0 rg BT /F1 9 Tf 303 28 Td ({i + 1}) Tj ET\n";
            Agregar($"<< /Length {encoding.GetByteCount(cuerpo)} >>\nstream\n{cuerpo}endstream");
        }
        using var pdf = new MemoryStream();
        void Escribir(string texto) => pdf.Write(encoding.GetBytes(texto));
        Escribir("%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var i = 0; i < objetos.Count; i++)
        {
            offsets.Add(pdf.Position);
            Escribir($"{i + 1} 0 obj\n"); pdf.Write(objetos[i]); Escribir("\nendobj\n");
        }
        var xref = pdf.Position;
        Escribir($"xref\n0 {offsets.Count}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1)) Escribir($"{offset:0000000000} 00000 n \n");
        Escribir($"trailer\n<< /Size {offsets.Count} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return pdf.ToArray();
    }
    private static readonly int[] Anchos =
    [
        761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761,
        761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761,
        278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
        1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
        667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
        333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
        556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584, 761,
        761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761,
        761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761, 761,
        278, 333, 556, 556, 556, 556, 260, 556, 333, 737, 370, 556, 584, 333, 737, 333,
        400, 584, 333, 333, 333, 556, 537, 278, 333, 333, 365, 556, 834, 834, 834, 611,
        667, 667, 667, 667, 667, 667, 1000, 722, 667, 667, 667, 667, 278, 278, 278, 278,
        722, 722, 778, 778, 778, 778, 778, 584, 778, 722, 722, 722, 722, 667, 667, 611,
        556, 556, 556, 556, 556, 556, 889, 500, 556, 556, 556, 556, 278, 278, 278, 278,
        556, 556, 556, 556, 556, 556, 556, 584, 611, 556, 556, 556, 556, 500, 556, 500,
    ];
}
