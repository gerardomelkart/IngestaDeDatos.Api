using ClosedXML.Excel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace IngestaDeDatos.Api.Fese;

public sealed class LectorFese
{
    public static string Normalizar(string texto)
    {
        var limpio = new string(texto.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
        return Regex.Replace(limpio.ToUpperInvariant(), @"[^A-Z0-9]+", " ").Trim();
    }

    private static string EntidadClave(string nombre) => Normalizar(nombre) switch
    {
        "DISTRITO FEDERAL" or "CDMX" => "CIUDAD DE MEXICO",
        "ESTADO DE MEXICO" => "MEXICO",
        "COAHUILA DE ZARAGOZA" => "COAHUILA",
        "MICHOACAN DE OCAMPO" => "MICHOACAN",
        "VERACRUZ DE IGNACIO DE LA LLAVE" => "VERACRUZ",
        var clave => clave
    };

    public LecturaFese Leer(byte[] archivo, IReadOnlyCollection<IncidenteFese> catalogo, string? entidadEsperada = null, DateTime? periodo = null)
    {
        var filas = new List<FilaFese>();
        var errores = new List<string>();
        var avisos = new List<string>();
        var claves = catalogo.Select(x => x.Codigo).ToHashSet();
        var vistas = new HashSet<(string, int)>();
        using var stream = new MemoryStream(archivo);
        using var libro = new XLWorkbook(stream);
        foreach (var hoja in libro.Worksheets)
        {
            var ultima = hoja.LastRowUsed()?.RowNumber() ?? 0;
            if (ultima > 100000 || (hoja.LastColumnUsed()?.ColumnNumber() ?? 0) > 200)
            {
                errores.Add($"Hoja {hoja.Name}: excede el tamaño admitido (100000 filas, 200 columnas).");
                continue;
            }
            var cabecera = 0;
            var colCodigo = 0;
            var colTotal = 0;
            var colCentro = 0;
            foreach (var fila in hoja.RowsUsed().Where(r => r.RowNumber() <= 100))
            {
                var celdas = fila.CellsUsed().ToList();
                var codigo = celdas.FirstOrDefault(c => Normalizar(c.GetString()) is "ID" or "CODIGO" or "CODIGO INCIDENTE" or "CLAVE" or "CLAVE INCIDENTE");
                var cantidad = celdas.FirstOrDefault(c => Normalizar(c.GetString()) is "CANTIDAD" or "TOTAL" or "LLAMADAS" or "TOTAL LLAMADAS");
                if (codigo is null || cantidad is null) continue;
                cabecera = fila.RowNumber();
                colCodigo = codigo.Address.ColumnNumber;
                colTotal = cantidad.Address.ColumnNumber;
                colCentro = celdas.FirstOrDefault(c => Normalizar(c.GetString()) is "CENTRO" or "NOMBRE DEL CENTRO")?.Address.ColumnNumber ?? 0;
                break;
            }
            if (cabecera == 0)
            {
                var auxiliar = Normalizar(hoja.Name) is "PORTADA" or "PRESENTACION" or "INSTRUCCIONES" or "LISTA" or "DATOS" or "RESUMEN";
                if (ultima > 0 && !auxiliar)
                    avisos.Add($"Hoja {hoja.Name}: no se identificaron encabezados de incidentes. Esta hoja no se incluirá; revise que no corresponda a un centro omitido.");
                continue;
            }
            // Metadatos ausentes no imponen un diseño fijo; si están declarados, deben coincidir.
            foreach (var celda in hoja.Range(1, 1, Math.Max(1, cabecera - 1), Math.Min(hoja.LastColumnUsed()?.ColumnNumber() ?? 1, 30)).CellsUsed())
            {
                var etiqueta = Normalizar(celda.GetString());
                if (etiqueta is not ("ANO" or "MES" or "ENTIDAD" or "ESTADO" or "NOMBRE DE LA ENTIDAD FEDERATIVA")) continue;
                var valor = Enumerable.Range(1, 3).Select(i => hoja.Cell(celda.Address.RowNumber, celda.Address.ColumnNumber + i).GetString().Trim()).FirstOrDefault(x => x.Length > 0);
                if (string.IsNullOrEmpty(valor)) continue;
                if (periodo.HasValue && etiqueta == "ANO" && int.TryParse(valor, out var anio) && anio != periodo.Value.Year)
                    errores.Add($"{hoja.Name}: el año declarado no coincide con el seleccionado.");
                if (periodo.HasValue && etiqueta == "MES")
                {
                    var mesDeclarado = int.TryParse(valor, out var numeroMes) ? numeroMes : Enumerable.Range(1, 12).FirstOrDefault(i => Normalizar(ExportacionesFese.Mes(i)) == Normalizar(valor));
                    if (mesDeclarado > 0 && mesDeclarado != periodo.Value.Month)
                        errores.Add($"{hoja.Name}: el mes declarado no coincide con el seleccionado.");
                }
                if (entidadEsperada is not null && etiqueta is "ENTIDAD" or "ESTADO" or "NOMBRE DE LA ENTIDAD FEDERATIVA")
                {
                    if (EntidadClave(valor) != EntidadClave(entidadEsperada))
                        errores.Add($"{hoja.Name}: la entidad declarada no coincide con la seleccionada.");
                }
            }
            var nombreCentro = "";
            foreach (var celda in hoja.Range(1, 1, cabecera, Math.Min(hoja.LastColumnUsed()?.ColumnNumber() ?? 1, 30)).CellsUsed())
            {
                if (Normalizar(celda.GetString()) is not ("NOMBRE DEL CENTRO" or "NOMBRE CENTRO")) continue;
                if (celda.Address.RowNumber == cabecera) continue;
                for (var i = 1; i <= 3; i++)
                {
                    var candidato = hoja.Cell(celda.Address.RowNumber, celda.Address.ColumnNumber + i).GetString().Trim();
                    if (candidato.Length == 0) continue;
                    nombreCentro = candidato;
                    break;
                }
                if (nombreCentro.Length > 0) break;
            }
            if (colCentro == 0 && nombreCentro.Length == 0)
            {
                errores.Add($"Hoja {hoja.Name}: no se identificó el nombre del centro; incluya la columna Centro o la etiqueta Nombre del Centro.");
                continue;
            }
            for (var n = cabecera + 1; n <= ultima; n++)
            {
                var textoCodigo = hoja.Cell(n, colCodigo).GetString().Trim();
                if (textoCodigo.Length == 0 || Normalizar(textoCodigo).StartsWith("TOTAL")) continue;
                if (!decimal.TryParse(textoCodigo, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var numeroCodigo) || numeroCodigo != decimal.Truncate(numeroCodigo))
                {
                    // Textos de sección no son filas de incidentes.
                    if (textoCodigo.Any(char.IsDigit)) errores.Add($"{hoja.Name}, fila {n}: código ilegible.");
                    continue;
                }
                if (numeroCodigo < 10000 || numeroCodigo > 99999)
                {
                    errores.Add($"{hoja.Name}, fila {n}: código fuera del catálogo.");
                    continue;
                }
                var codigo = (int)numeroCodigo;
                if (!claves.Contains(codigo))
                {
                    errores.Add($"{hoja.Name}, fila {n}: código {codigo} no registrado en el catálogo.");
                    continue;
                }
                var centro = colCentro > 0 ? hoja.Cell(n, colCentro).GetString().Trim() : nombreCentro;
                if (centro.Length is 0 or > 160)
                {
                    errores.Add($"{hoja.Name}, fila {n}: indique un centro de hasta 160 caracteres.");
                    continue;
                }
                var texto = hoja.Cell(n, colTotal).GetString().Trim();
                if (!decimal.TryParse(texto, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var total) || total < 0 || total != decimal.Truncate(total) || total > 999999999999L)
                {
                    avisos.Add($"{hoja.Name}, fila {n}: cantidad vacía o inválida; se registró como 0.");
                    total = 0;
                }
                if (!vistas.Add((Normalizar(centro), codigo)))
                {
                    errores.Add($"Centro {centro}: el código {codigo} aparece más de una vez. No se sumará automáticamente.");
                    continue;
                }
                filas.Add(new(centro, codigo, (long)total));
            }
        }
        if (filas.Count == 0) errores.Add("No se encontraron incidentes legibles. Incluya código, cantidad y nombre del centro.");
        foreach (var centro in filas.GroupBy(x => Normalizar(x.Centro)))
        {
            var nombre = centro.First().Centro;
            if (centro.Count() < claves.Count) avisos.Add($"{nombre}: se recibieron {centro.Count()} de {claves.Count} códigos. Los no informados no se convertirán en cero.");
            if (centro.Sum(x => x.Total) == 0) avisos.Add($"{nombre}: el total del centro es cero.");
        }
        return new(filas, errores.Take(300).ToList(), avisos);
    }
}
