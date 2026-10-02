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

public sealed class DdcpService(DbFactory factory)
{
    private static readonly string[] Meses = ["", "ENERO", "FEBRERO", "MARZO", "ABRIL", "MAYO", "JUNIO", "JULIO", "AGOSTO", "SEPTIEMBRE", "OCTUBRE", "NOVIEMBRE", "DICIEMBRE"];
    private const string ConsultaSql = "SELECT ID_ENTIDAD IdEntidad, NOMBRE_ENTIDAD NombreEntidad, NUM_DIS_DECOMISADOS Dispositivos, PERSONAS_P_DISPOSICION Personas, REVISION Revision, HABILITADO Habilitado, PERIODO Periodo FROM dbo.DISPOSITIVOS_DECOMISADOS WHERE YEAR(PERIODO) = @anio AND (@mes IS NULL OR MONTH(PERIODO) = @mes) AND (@entidad IS NULL OR ID_ENTIDAD = @entidad);";
    public static void PeriodoValido(int anio, int? mes)
    {
        if (anio < 2000 || anio > 2100 || mes.HasValue && (mes < 1 || mes > 12)) throw new ArgumentException("Periodo inválido: año 2000 a 2100 y mes 1 a 12.");
    }
    public async Task<List<Entidad>> Entidades(UsuarioInfo usuario)
    {
        await using var db = factory.Modulo("DDCP");
        return (await db.QueryAsync<Entidad>("SELECT ID_ENTIDAD IdEntidad, NOMBRE_ENTIDAD NombreEntidad, NOMBRE_CORTO NombreCorto FROM dbo.CAT_ENTIDAD WHERE HABILITADO = 1 AND (@id IS NULL OR ID_ENTIDAD = @id) ORDER BY ID_ENTIDAD;", new { id = usuario.EsNacional ? null : usuario.IdEntidad })).ToList();
    }
    public async Task<List<RegistroDdcp>> Consultar(UsuarioInfo usuario, int anio, int? mes, int? entidad, bool incluirInactivos = false)
    {
        PeriodoValido(anio, mes);
        if (!usuario.EsNacional && entidad.HasValue && entidad != usuario.IdEntidad) throw new UnauthorizedAccessException("No puede consultar otra entidad.");
        entidad = usuario.EsNacional ? entidad : usuario.IdEntidad;
        await using var db = factory.Modulo("DDCP");
        var registros = await db.QueryAsync<RegistroDdcp>(ConsultaSql, new { anio, mes, entidad });
        return registros.Where(x => incluirInactivos || x.Habilitado).OrderBy(x => x.Periodo).ThenBy(x => x.IdEntidad).ToList();
    }
    public async Task<PreviaDdcp> Preparar(UsuarioInfo usuario, int anio, int mes, string hoja, IFormFile archivo)
    {
        PeriodoValido(anio, mes);
        if (archivo.Length == 0 || archivo.Length > 10 * 1024 * 1024 || !string.Equals(Path.GetExtension(archivo.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Suba un archivo .xlsx de hasta 10 MB.");
        var entidades = await Entidades(usuario);
        var porNombre = entidades.ToDictionary(x => Normalizar(x.NombreEntidad));
        using var stream = new MemoryStream();
        await archivo.CopyToAsync(stream);
        stream.Position = 0;
        using var workbook = Abrir(stream);
        var sheet = workbook.Worksheets.FirstOrDefault(x => string.Equals(x.Name, hoja, StringComparison.OrdinalIgnoreCase)) ?? throw new ArgumentException("La hoja seleccionada no existe.");
        var encabezado = sheet.RowsUsed().FirstOrDefault(r => Normalizar(r.Cell(2).GetString()) == "ENTIDAD FEDERATIVA") ?? throw new ArgumentException("No se encontró el encabezado: Núm., Entidad Federativa, Número de dispositivos decomisados, Personas puestas a disposición.");
        if (Normalizar(encabezado.Cell(1).GetString()) != "NUM." || Normalizar(encabezado.Cell(3).GetString()) != "NUMERO DE DISPOSITIVOS DECOMISADOS" || Normalizar(encabezado.Cell(4).GetString()) != "PERSONAS PUESTAS A DISPOSICION") throw new ArgumentException("Los cuatro encabezados del archivo no corresponden a DDCP.");
        var titulo = string.Join(" ", sheet.RowsUsed().Where(r => r.RowNumber() < encabezado.RowNumber()).SelectMany(r => r.CellsUsed()).Select(c => Normalizar(c.GetString())));
        if (!Regex.IsMatch(titulo, $@"\b{anio}\b") || !Regex.IsMatch(titulo, $@"\b{Meses[mes]}\b")) throw new ArgumentException("El año y mes del encabezado no coinciden con el periodo seleccionado.");
        var errores = new List<string>();
        var advertencias = new List<string>();
        var filas = new List<FilaDdcp>();
        var ids = new HashSet<int>();
        var anteriores = (await Consultar(usuario, anio, mes, null, true)).ToDictionary(x => x.IdEntidad);
        decimal? totalDispositivos = null;
        decimal? totalPersonas = null;
        foreach (var row in sheet.RowsUsed().Where(r => r.RowNumber() > encabezado.RowNumber()))
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
        await db.ExecuteAsync("INSERT dbo.DDCP_CARGA(ID_CARGA, ID_USUARIO, PERIODO, ESTADO, NOMBRE_ARCHIVO, ARCHIVO, FILAS_JSON) VALUES (@id, @usuario, @periodo, 'PREVIA', @nombre, @bytes, @json);", new { id, usuario = usuario.IdUsuario, periodo = new DateTime(anio, mes, 1), nombre = Path.GetFileName(archivo.FileName)[..Math.Min(Path.GetFileName(archivo.FileName).Length, 255)], bytes = stream.ToArray(), json = JsonSerializer.Serialize(filas) });
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
    public async Task Confirmar(UsuarioInfo usuario, Guid id)
    {
        await using var db = factory.Modulo("DDCP");
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        var carga = await db.QuerySingleOrDefaultAsync<CargaInfo>("SELECT ID_CARGA IdCarga, ID_USUARIO IdUsuario, PERIODO Periodo, ESTADO Estado, FILAS_JSON FilasJson FROM dbo.DDCP_CARGA WITH (UPDLOCK, HOLDLOCK) WHERE ID_CARGA = @id;", new { id }, tx) ?? throw new ArgumentException("La carga no existe.");
        if (carga.IdUsuario != usuario.IdUsuario) throw new UnauthorizedAccessException("La carga pertenece a otro usuario.");
        if (carga.Estado != "PREVIA") throw new ArgumentException("La carga ya fue confirmada o cancelada.");
        var filas = JsonSerializer.Deserialize<List<FilaDdcp>>(carga.FilasJson) ?? throw new ArgumentException("Carga sin filas.");
        foreach (var fila in filas.OrderBy(x => x.IdEntidad))
        {
            if (!usuario.EsNacional && fila.IdEntidad != usuario.IdEntidad) throw new UnauthorizedAccessException("Su alcance cambió. Prepare otra carga.");
            if (!await db.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS (SELECT 1 FROM dbo.CAT_ENTIDAD WHERE ID_ENTIDAD = @entidad AND HABILITADO = 1) THEN 1 ELSE 0 END;", new { entidad = fila.IdEntidad }, tx)) throw new ArgumentException("Una entidad de la carga fue deshabilitada. Prepare otra carga.");
            var actual = await db.QuerySingleOrDefaultAsync<RegistroDdcp>("SELECT NUM_DIS_DECOMISADOS Dispositivos, PERSONAS_P_DISPOSICION Personas, REVISION Revision, HABILITADO Habilitado FROM dbo.DISPOSITIVOS_DECOMISADOS WITH (UPDLOCK, HOLDLOCK) WHERE ID_ENTIDAD = @entidad AND PERIODO = @periodo;", new { entidad = fila.IdEntidad, periodo = carga.Periodo }, tx);
            if (actual?.Revision != fila.RevisionAntes || actual?.Dispositivos.ToString("0", CultureInfo.InvariantCulture) != fila.DispositivosAntes || actual?.Personas != fila.PersonasAntes || actual?.Habilitado != fila.HabilitadoAntes) throw new ArgumentException("Los datos cambiaron después de la vista previa. Cancele esta carga y vuelva a validar el archivo.");
            var parametros = new { entidad = fila.IdEntidad, periodo = carga.Periodo, dispositivos = decimal.Parse(fila.Dispositivos, CultureInfo.InvariantCulture), personas = fila.Personas, nombre = fila.NombreEntidad, id, usuario = usuario.IdUsuario };
            if (actual is null)
                await db.ExecuteAsync("INSERT dbo.DISPOSITIVOS_DECOMISADOS(ID_DISPOSITIVO_DECOMISADO, NOMBRE_ENTIDAD, NUM_DIS_DECOMISADOS, PERSONAS_P_DISPOSICION, FECHA_CREACION, HABILITADO, ID_ENTIDAD, PERIODO, FECHA_INGESTA, REVISION) VALUES (NEXT VALUE FOR dbo.SEQ_DECOMISO, @nombre, @dispositivos, @personas, GETDATE(), 1, @entidad, @periodo, SYSUTCDATETIME(), 1);", parametros, tx);
            else if (actual.Dispositivos != parametros.dispositivos || actual.Personas != fila.Personas || !actual.Habilitado)
            {
                await db.ExecuteAsync("INSERT dbo.DDCP_HISTORICO(ID_DISPOSITIVO_DECOMISADO, ID_ENTIDAD, NOMBRE_ENTIDAD, PERIODO, NUM_DIS_DECOMISADOS, PERSONAS_P_DISPOSICION, REVISION, HABILITADO, FECHA_CREACION, FECHA_MODIFICACION, FECHA_INGESTA, ID_CARGA, ID_USUARIO) SELECT ID_DISPOSITIVO_DECOMISADO, ID_ENTIDAD, NOMBRE_ENTIDAD, PERIODO, NUM_DIS_DECOMISADOS, PERSONAS_P_DISPOSICION, REVISION, HABILITADO, FECHA_CREACION, FECHA_MODIFICACION, FECHA_INGESTA, @id, @usuario FROM dbo.DISPOSITIVOS_DECOMISADOS WHERE ID_ENTIDAD = @entidad AND PERIODO = @periodo;", parametros, tx);
                await db.ExecuteAsync("UPDATE dbo.DISPOSITIVOS_DECOMISADOS SET NUM_DIS_DECOMISADOS = @dispositivos, PERSONAS_P_DISPOSICION = @personas, FECHA_MODIFICACION = GETDATE(), FECHA_INGESTA = SYSUTCDATETIME(), REVISION = REVISION + 1, HABILITADO = 1 WHERE ID_ENTIDAD = @entidad AND PERIODO = @periodo;", parametros, tx);
            }
        }
        await db.ExecuteAsync("UPDATE dbo.DDCP_CARGA SET ESTADO = 'CONFIRMADA', FECHA_CONFIRMACION = SYSUTCDATETIME() WHERE ID_CARGA = @id;", new { id }, tx);
        await tx.CommitAsync();
    }
    public async Task Cancelar(UsuarioInfo usuario, Guid id)
    {
        await using var db = factory.Modulo("DDCP");
        if (await db.ExecuteAsync("UPDATE dbo.DDCP_CARGA SET ESTADO = 'CANCELADA' WHERE ID_CARGA = @id AND ID_USUARIO = @usuario AND ESTADO = 'PREVIA';", new { id, usuario = usuario.IdUsuario }) != 1) throw new ArgumentException("La carga no existe, pertenece a otro usuario o ya terminó.");
    }
    public async Task<byte[]> Plantilla(UsuarioInfo usuario, int anio, int mes)
    {
        PeriodoValido(anio, mes);
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet(CultureInfo.GetCultureInfo("es-MX").DateTimeFormat.GetMonthName(mes));
        sheet.Cell(1, 1).Value = "INFORME DE PUESTA DE DISPOSITIVOS A DISPOSICIÓN";
        sheet.Range(1, 1, 1, 4).Merge();
        sheet.Cell(2, 1).Value = $"01 AL {DateTime.DaysInMonth(anio, mes)} DE {Meses[mes]} {anio}";
        sheet.Range(2, 1, 2, 4).Merge();
        string[] columnas = ["Núm.", "Entidad Federativa", "Número de dispositivos decomisados", "Personas puestas a disposición"];
        for (var c = 0; c < columnas.Length; c++) sheet.Cell(4, c + 1).Value = columnas[c];
        var entidades = await Entidades(usuario);
        for (var i = 0; i < entidades.Count; i++) { sheet.Cell(i + 5, 1).Value = i + 1; sheet.Cell(i + 5, 2).Value = entidades[i].NombreEntidad; }
        sheet.Columns(1, 4).Width = 28;
        sheet.Column(2).Width = 30;
        sheet.Row(1).Height = 36;
        sheet.Row(4).Height = 36;
        sheet.Range(1, 1, entidades.Count + 4, 4).Style.Alignment.WrapText = true;
        sheet.Range(4, 1, 4, 4).Style.Font.Bold = true;
        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        return stream.ToArray();
    }
}

