using IngestaDeDatos.Api.Configuration;
using Microsoft.Extensions.Options;
using ClosedXML.Excel;
using IngestaDeDatos.Api.Models;
using IngestaDeDatos.Api.Repositories;
using IngestaDeDatos.Api.Validators;
using static IngestaDeDatos.Api.Readers.DdcpArchivoReader;
using System.Globalization;
using System.Text.Json;

namespace IngestaDeDatos.Api.Services;

public sealed class DdcpService(IDdcpRepository repository, ICatalogosRepository catalogos, IOptions<ReglasDdcpOptions> opciones)
{
    private ReglasDdcpOptions Reglas => opciones.Value;
    public void PeriodoValido(int anio, int? mes)
    {
        DdcpPeriodoValidator.Validar(anio, mes, Reglas);
    }
    public async Task<List<Entidad>> Entidades(UsuarioInfo usuario)
    {
        return await repository.Entidades(usuario.EsNacional ? null : usuario.IdEntidad);
    }
    public async Task<List<RegistroDdcp>> Consultar(UsuarioInfo usuario, int anio, int? mes, int? entidad, bool incluirInactivos = false)
    {
        PeriodoValido(anio, mes);
        if (!usuario.EsNacional && entidad.HasValue && entidad != usuario.IdEntidad) throw new UnauthorizedAccessException("No puede consultar otra entidad.");
        entidad = usuario.EsNacional ? entidad : usuario.IdEntidad;
        var registros = await repository.Consultar(anio, mes, entidad);
        return registros.Where(x => incluirInactivos || x.Habilitado).OrderBy(x => x.Periodo).ThenBy(x => x.IdEntidad).ToList();
    }
    public async Task<byte[]> ExportarConsulta(UsuarioInfo usuario, int anio, int? mes, int? entidad, FiltrosTablaDdcp? filtros = null)
    {
        var filas = await Consultar(usuario, anio, mes, entidad);
        var alcance = usuario.EsNacional ? entidad : usuario.IdEntidad;
        var meses = await catalogos.Meses();
        var visibles = TablaConsultaDdcp.Aplicar(filas, meses, filtros ?? new FiltrosTablaDdcp());
        return ExportacionDdcp.Generar(visibles, anio, mes, alcance, meses, await catalogos.NombreModulo());
    }
    public async Task<PreviaDdcp> Preparar(UsuarioInfo usuario, int anio, int mes, IFormFile archivo)
    {
        PeriodoValido(anio, mes);
        if (archivo.Length == 0 || archivo.Length > Reglas.MaxArchivoBytes || !string.Equals(Path.GetExtension(archivo.FileName), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"Suba un archivo .xlsx de hasta {Reglas.MaxArchivoBytes / 1024m / 1024m:0.##} MB.");
        var entidades = await Entidades(usuario);
        var meses = await catalogos.Meses();
        var porNombre = entidades.ToDictionary(x => Normalizar(x.NombreEntidad));
        using var stream = new MemoryStream();
        await archivo.CopyToAsync(stream);
        stream.Position = 0;
        using var workbook = Abrir(stream);
        var sheet = SeleccionarHoja(workbook, anio, mes, meses);
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
        var advertencias = new List<string> { $"La información se registrará en {meses.Single(x => x.IdMes == mes).Nombre.ToLowerInvariant()} de {anio}, conforme al periodo seleccionado en pantalla." };
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
            if (!Numero(row.Cell(3), out var dispositivos) || dispositivos > Reglas.MaxDispositivos) { errores.Add($"Fila {row.RowNumber()}: dispositivos debe ser un entero no negativo hasta {Reglas.MaxDispositivos:0}, sin notas ni fórmulas."); continue; }
            if (!Numero(row.Cell(4), out var personas) || personas > Reglas.MaxPersonas) { errores.Add($"Fila {row.RowNumber()}: personas debe ser un entero no negativo hasta {Reglas.MaxPersonas}, sin notas ni fórmulas."); continue; }
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
        await repository.CrearVistaPrevia(id, usuario.IdUsuario, new DateTime(anio, mes, 1), Path.GetFileName(archivo.FileName)[..Math.Min(Path.GetFileName(archivo.FileName).Length, 255)], stream.ToArray(), JsonSerializer.Serialize(filas));
        return new PreviaDdcp(id, anio, mes, filas, advertencias);
    }
    public Task Confirmar(UsuarioInfo usuario, Guid id) => repository.Confirmar(usuario, id);
    public Task Cancelar(UsuarioInfo usuario, Guid id) => repository.Cancelar(usuario, id);
    public async Task<byte[]> Plantilla(UsuarioInfo usuario, int anio, int mes)
    {
        PeriodoValido(anio, mes);
        var meses = await catalogos.Meses();
        using var workbook = new XLWorkbook();
        var sheet = workbook.AddWorksheet((await catalogos.Meses()).Single(x => x.IdMes == mes).Nombre);
        sheet.Cell(1, 1).Value = await catalogos.NombreModulo();
        sheet.Range(1, 1, 1, 4).Merge();
        sheet.Cell(2, 1).Value = $"01 AL {DateTime.DaysInMonth(anio, mes)} DE {meses.Single(x => x.IdMes == mes).Nombre.ToUpperInvariant()} {anio}";
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

