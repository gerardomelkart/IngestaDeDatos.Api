using ClosedXML.Excel;
using Dapper;
using IngestaDeDatos.Api.Data;
using IngestaDeDatos.Api.Models;
using System.Data;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace IngestaDeDatos.Api.Services;

public sealed partial class DdcpService
{    private static IXLRow? BuscarEncabezado(IXLWorksheet sheet)
    {
        return sheet.RowsUsed().FirstOrDefault(row => Normalizar(row.Cell(2).GetString()) == "ENTIDAD FEDERATIVA");
    }

    private static bool EsFilaDeDatos(IXLRow row)
    {
        return Regex.IsMatch(row.Cell(1).GetString().Trim(), @"^\d+$") && !string.IsNullOrWhiteSpace(row.Cell(2).GetString());
    }

    private static IXLWorksheet SeleccionarHoja(XLWorkbook workbook, int anio, int mes)
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
    public async Task<PreviaDdcp> Preparar(UsuarioInfo usuario, int anio, int mes, IFormFile archivo)
    {
        PeriodoValido(anio, mes);
        if (archivo.Length == 0 || archivo.Length > 10 * 1024 * 1024 || !string.Equals(Path.GetExtension(archivo.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Suba un archivo .xlsx de hasta 10 MB.");
        var entidades = await Entidades(usuario);
        var porNombre = entidades.ToDictionary(x => Normalizar(x.NombreEntidad));
        using var stream = new MemoryStream();
        await archivo.CopyToAsync(stream);
        stream.Position = 0;
        using var workbook = Abrir(stream);
        var sheet = SeleccionarHoja(workbook, anio, mes);
        var encabezado = BuscarEncabezado(sheet);
        var primeraFila = encabezado is null ? sheet.RowsUsed().First(EsFilaDeDatos).RowNumber() : encabezado.RowNumber() + 1;
        if (encabezado is not null)
        {
            if (Normalizar(encabezado.Cell(1).GetString()) != "NUM." || Normalizar(encabezado.Cell(3).GetString()) != "NUMERO DE DISPOSITIVOS DECOMISADOS" || Normalizar(encabezado.Cell(4).GetString()) != "PERSONAS PUESTAS A DISPOSICION")
            {
                throw new ArgumentException("Los cuatro encabezados del archivo no corresponden a DDCP.");
            }
        }
        var errores = new List<string>();
        var advertencias = new List<string> { $"La información se registrará en {Meses[mes].ToLowerInvariant()} de {anio}, conforme al periodo seleccionado en pantalla." };
        var filas = new List<FilaDdcp>();
        var ids = new HashSet<int>();
        var anteriores = (await Consultar(usuario, anio, mes, null, true)).ToDictionary(x => x.IdEntidad);
        decimal? totalDispositivos = null;
        decimal? totalPersonas = null;
        foreach (var row in sheet.RowsUsed().Where(r => r.RowNumber() >= primeraFila))
        {
            var textoPrimero = Normalizar(row.Cell(1).GetString());
            if (textoPrimero.StartsWith("TOTAL"))
            {
                if (Numero(row.Cell(3), out var td)) totalDispositivos = td;
                if (Numero(row.Cell(4), out var tp)) totalPersonas = tp;
                break;
            }
            var nombre = row.Cell(2).GetString().Trim();
            if (string.IsNullOrWhiteSpace(nombre) && row.CellsUsed().All(c => c.IsEmpty())) continue;
            if (string.IsNullOrWhiteSpace(nombre)) { errores.Add($"Fila {row.RowNumber()}: falta la entidad o hay una fila de datos sin identificar."); continue; }
            if (!porNombre.TryGetValue(Normalizar(nombre), out var entidad)) { errores.Add($"Fila {row.RowNumber()}: entidad desconocida o fuera de su alcance: {nombre}."); continue; }
            if (!ids.Add(entidad.IdEntidad)) { errores.Add($"Fila {row.RowNumber()}: entidad repetida: {nombre}."); continue; }
            if (!Numero(row.Cell(3), out var dispositivos) || dispositivos > 999999999999999999m) { errores.Add($"Fila {row.RowNumber()}: dispositivos debe ser un entero no negativo de hasta 18 dígitos, sin notas ni fórmulas."); continue; }
            if (!Numero(row.Cell(4), out var personas) || personas > int.MaxValue) { errores.Add($"Fila {row.RowNumber()}: personas debe ser un entero no negativo hasta {int.MaxValue}, sin notas ni fórmulas."); continue; }
            anteriores.TryGetValue(entidad.IdEntidad, out var antes);
            filas.Add(new FilaDdcp(entidad.IdEntidad, entidad.NombreEntidad, dispositivos.ToString("0", CultureInfo.InvariantCulture), (int)personas, antes?.Dispositivos.ToString("0", CultureInfo.InvariantCulture), antes?.Personas, antes?.Revision, antes?.Habilitado));
        }
        if (filas.Count != entidades.Count) errores.Add($"Se requieren las {entidades.Count} entidades de su alcance, sin omisiones. Un cero reportado debe aparecer explícitamente.");
        if (errores.Count > 0) throw new ArgumentException(string.Join("\n", errores));
        var sumaDispositivos = filas.Sum(x => decimal.Parse(x.Dispositivos, CultureInfo.InvariantCulture));
        var sumaPersonas = filas.Sum(x => (long)x.Personas);
        if (totalDispositivos.HasValue && totalDispositivos != sumaDispositivos) advertencias.Add($"El total de dispositivos escrito ({totalDispositivos}) difiere de la suma de filas ({sumaDispositivos}). Se usará la suma de filas.");
        if (totalPersonas.HasValue && totalPersonas != sumaPersonas) advertencias.Add($"El total de personas escrito ({totalPersonas}) difiere de la suma de filas ({sumaPersonas}). Se usará la suma de filas.");
        if (filas.Any(x => x.RevisionAntes.HasValue)) advertencias.Add("El periodo contiene registros existentes. La confirmación reemplaza sus cantidades, conserva histórico de los cambios y no vuelve a sumarlas.");
        var id = Guid.NewGuid();
        await using var db = factory.Modulo("DDCP");
        await db.ExecuteAsync(DdcpSql.CrearVistaPrevia, new { id, usuario = usuario.IdUsuario, periodo = new DateTime(anio, mes, 1), nombre = Path.GetFileName(archivo.FileName)[..Math.Min(Path.GetFileName(archivo.FileName).Length, 255)], bytes = stream.ToArray(), json = JsonSerializer.Serialize(filas) });
        return new PreviaDdcp(id, anio, mes, filas, advertencias);
    }
    private static XLWorkbook Abrir(Stream stream)
    {
        try { return new XLWorkbook(stream); }
        catch (Exception ex) when (ex is not OutOfMemoryException) { throw new ArgumentException("No fue posible leer el Excel. Verifique que sea .xlsx válido y no esté protegido."); }
    }
    private static bool Numero(IXLCell cell, out decimal numero)
    {
        numero = 0;
        if (cell.HasFormula) return false;
        var texto = cell.GetString().Trim();
        return Regex.IsMatch(texto, @"^\d+$") && decimal.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out numero) && numero >= 0;
    }
    private static string Normalizar(string value) => new string(value.Trim().Normalize(NormalizationForm.FormD).Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray()).Normalize(NormalizationForm.FormC).ToUpperInvariant();

}
