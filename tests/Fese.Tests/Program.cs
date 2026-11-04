using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.FileProviders;
using IngestaDeDatos.Api.Data;
using ClosedXML.Excel;
using IngestaDeDatos.Api.Fese;

var catalogo = new List<IncidenteFese> { new() { Codigo = 10101, Nombre = "Ejemplo", Categoria = "MÉDICO", Procedente = true } };
using var libro = new XLWorkbook();
var hoja = libro.AddWorksheet("Centro diferente");
hoja.Cell(4, 3).Value = "Centro";
hoja.Cell(4, 4).Value = "Código";
hoja.Cell(4, 5).Value = "Cantidad";
hoja.Cell(5, 3).Value = "Centro ficticio";
hoja.Cell(5, 4).Value = 10101;
hoja.Cell(5, 5).Value = 12;

var lector = new LectorFese();
var resultado = lector.Leer(Guardar(libro), catalogo);
Assert(resultado.Errores.Count == 0 && resultado.Filas.Single().Total == 12, "Lee encabezados desplazados y nombre libre de hoja");
hoja.Cell(5, 5).Clear();

Assert(lector.Leer(Guardar(libro), catalogo).Errores.Count > 0, "Vacío no se convierte en cero");
hoja.Cell(5, 5).Value = -1;

Assert(lector.Leer(Guardar(libro), catalogo).Errores.Count > 0, "Rechaza negativos");
hoja.Cell(5, 5).Value = 1.5;

Assert(lector.Leer(Guardar(libro), catalogo).Errores.Count > 0, "Rechaza decimales");
hoja.Cell(5, 5).Value = "1,5";
Assert(lector.Leer(Guardar(libro), catalogo).Errores.Count > 0, "No interpreta decimal con coma como quince");
hoja.Cell(5, 5).Value = 12;
hoja.Cell(6, 3).Value = "Centro ficticio";
hoja.Cell(6, 4).Value = 10101;
hoja.Cell(6, 5).Value = 5;
Assert(lector.Leer(Guardar(libro), catalogo).Errores.Count > 0, "No suma códigos duplicados");
hoja.Row(6).Clear();
hoja.Cell(1, 1).Value = "Año:";
hoja.Cell(1, 2).Value = 2025;
Assert(lector.Leer(Guardar(libro), catalogo, "Chiapas", new DateTime(2026, 9, 1)).Errores.Count > 0, "Rechaza periodo declarado distinto");
var u = new IngestaDeDatos.Api.Models.UsuarioInfo { EsNacional = false, IdEntidad = 7 };
Assert(AccesoFese.Entidad(u, null) == 7, "El enlace conserva su entidad");
try { AccesoFese.Entidad(u, 1); throw new Exception("Se permitió otra entidad"); }
catch (UnauthorizedAccessException) { Console.WriteLine("OK: Impide otra entidad"); }
var entidades = Enumerable.Range(1, 32).Select(i => new EntidadFese(i, "Entidad " + i)).ToList();
var cargas = new List<CargaFese> { new() { IdEntidad = 1, Estado = "ACEPTADA", Periodo = new(2026, 9, 1) } };
var datos = new List<DatoFese> { new() { IdEntidad = 1, Entidad = "Entidad 1", Periodo = new(2026, 9, 1), Centro = "Centro ficticio", Codigo = 10101, Incidente = "Ejemplo", Categoria = "MÉDICO", Procedente = true, Total = 12 } };
using (var cniedt = new XLWorkbook(new MemoryStream(ExportacionesFese.Cniedt(datos, cargas, entidades, 2026))))
{
    Assert(cniedt.Worksheet("Llamadas 911").Cell(3, 10).GetValue<long>() == 12, "CNIEDT ubica septiembre en columna J");
    Assert(cniedt.Worksheet("Llamadas 911").Cell(4, 10).IsEmpty(), "CNIEDT no inventa ceros para entidades sin entrega");
    Assert(cniedt.Worksheet("Llamadas 911").Cell(35, 10).IsEmpty(), "CNIEDT no presenta nacional incompleto como completo");
}
var pdf = ExportacionesFese.Acuse(new CargaFese { IdCarga = Guid.NewGuid(), Entidad = "Entidad ficticia", Periodo = new(2026, 9, 1), Tipo = "INICIAL", Usuario = "Prueba", NombreArchivo = "formato-ficticio.xlsx", FechaCreacion = RepositorioFese.Ahora() }, [new("Centro ficticio",10101,12)], [], catalogo, false);
Assert(System.Text.Encoding.Latin1.GetString(pdf).StartsWith("%PDF-1.4"), "Genera acuse PDF");
var qa = Path.Combine(Path.GetTempPath(), "fese-test-exports");
Directory.CreateDirectory(qa);
File.WriteAllBytes(Path.Combine(qa, "acuse.pdf"), pdf);
File.WriteAllBytes(Path.Combine(qa, "cniedt.xlsx"), ExportacionesFese.Cniedt(datos, cargas, entidades, 2026));
var formato = Environment.GetEnvironmentVariable("FESE_TEST_FORMATO");
var rutaCatalogo = Environment.GetEnvironmentVariable("FESE_TEST_CATALOGO");
if (formato is not null && rutaCatalogo is not null)
{
    var realCatalogo = System.Text.Json.JsonSerializer.Deserialize<List<IncidenteFese>>(File.ReadAllText(rutaCatalogo))!;
    var real = lector.Leer(File.ReadAllBytes(formato), realCatalogo, "Chiapas", new DateTime(2026,9,1));
    Assert(real.Errores.Count == 0 && real.Filas.Count == 2820, "Lee formato FESE de diez centros y 282 claves sin depender de hojas numeradas");
}
using (var diferencias = new XLWorkbook(new MemoryStream(ExportacionesFese.Diferencias([new("Centro anterior", 10101, 10)], [new("Centro nuevo", 10101, 20)]))))
{
    Assert(diferencias.Worksheet(1).LastRowUsed()!.RowNumber() == 3, "La exportación conserva altas y bajas de centros");
}
hoja.Cell(1, 2).Value = 2026;
var desconocida = libro.AddWorksheet("Centro sin encabezados");
desconocida.Cell(1,1).Value = "Datos no reconocidos";
Assert(lector.Leer(Guardar(libro), catalogo).Advertencias.Any(x => x.Contains("Centro sin encabezados")), "Advierte hojas que no fueron interpretadas");
var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string,string?>
{
    ["Modulos:FESE:Conexion"] = "FESE",
    ["ConnectionStrings:FESE"] = "Server=localhost;Initial Catalog=FESE_DEV;Integrated Security=true;TrustServerCertificate=true"
}).Build();
using (var dev = new DbFactory(config, new EntornoPrueba("Development")).Modulo("FESE"))
    Assert(dev.Database == "FESE_DEV", "Desarrollo selecciona FESE_DEV");
try { using var incorrecta = new DbFactory(config, new EntornoPrueba("Production")).Modulo("FESE"); throw new Exception("Producción aceptó base DEV"); }
catch (InvalidOperationException) { Console.WriteLine("OK: Producción rechaza una conexión a FESE_DEV"); }
Console.WriteLine("Pruebas correctas");

static void Assert(bool valor, string mensaje)
{
    if (!valor) throw new Exception(mensaje);
    Console.WriteLine("OK: " + mensaje);
}

static byte[] Guardar(XLWorkbook libro)
{
    var nuevo = new MemoryStream();
    libro.SaveAs(nuevo);
    return nuevo.ToArray();
}

sealed class EntornoPrueba(string ambiente) : IHostEnvironment
{
    public string EnvironmentName { get; set; } = ambiente;
    public string ApplicationName { get; set; } = "Prueba";
    public string ContentRootPath { get; set; } = ".";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
