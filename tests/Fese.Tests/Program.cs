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

resultado = lector.Leer(Guardar(libro), catalogo);
Assert(resultado.Errores.Count == 0 && resultado.Filas.Single().Total == 0 && resultado.Advertencias.Any(a => a.Contains("se registró como 0")), "Cantidad normalizada a cero con advertencia");
hoja.Cell(5, 5).Value = -1;

resultado = lector.Leer(Guardar(libro), catalogo);
Assert(resultado.Errores.Count == 0 && resultado.Filas.Single().Total == 0 && resultado.Advertencias.Any(a => a.Contains("se registró como 0")), "Cantidad normalizada a cero con advertencia");
hoja.Cell(5, 5).Value = 1.5;

resultado = lector.Leer(Guardar(libro), catalogo);
Assert(resultado.Errores.Count == 0 && resultado.Filas.Single().Total == 0 && resultado.Advertencias.Any(a => a.Contains("se registró como 0")), "Cantidad normalizada a cero con advertencia");
hoja.Cell(5, 5).Value = "1,5";
resultado = lector.Leer(Guardar(libro), catalogo);
Assert(resultado.Errores.Count == 0 && resultado.Filas.Single().Total == 0 && resultado.Advertencias.Any(a => a.Contains("se registró como 0")), "Cantidad normalizada a cero con advertencia");
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
Assert(System.Text.Encoding.Latin1.GetString(pdf).Contains("Comparativo contra el mes anterior"), "El comprobante sigue el formato mensual institucional");
var textoPrevio = System.Text.Encoding.Latin1.GetString(pdf);
Assert(textoPrevio.Contains("(PREVIO) Tj") && textoPrevio.Contains("0.81915 0.57358 -0.57358 0.81915"), "La vista previa lleva marca de agua diagonal como SIIID");
Assert(!textoPrevio.Contains("VISTA PREVIA -") && !textoPrevio.Contains("Este documento no acredita"), "El aviso previo no se inserta en el párrafo");
Assert(!textoPrevio.Contains("Folio:") && !textoPrevio.Contains("Usuario de carga:"), "El comprobante no muestra folio ni usuario");
var enero = new DateTime(2027, 1, 4);
Assert(PoliticaEntregaFese.Corte(enero) == new DateTime(2026, 12, 1), "El corte de enero corresponde a diciembre del año anterior");
PoliticaEntregaFese.Validar(new(2026, 12, 1), false, false, enero);
PoliticaEntregaFese.Validar(new(2026, 9, 1), true, true, enero);
Rechaza(() => PoliticaEntregaFese.Validar(new(2026, 11, 1), false, false, enero), "Una carga inicial no admite otro corte");
Rechaza(() => PoliticaEntregaFese.Validar(new(2026, 12, 1), false, true, enero), "Una entrega existente se envía por Actualizaciones");
Rechaza(() => PoliticaEntregaFese.Validar(new(2026, 9, 1), true, false, enero), "Una actualización necesita entrega aceptada");
Rechaza(() => PoliticaEntregaFese.Validar(new(2027, 1, 1), true, true, enero), "Las actualizaciones no admiten meses sin cerrar");
var qa = Path.Combine(Path.GetTempPath(), "fese-test-exports");
Directory.CreateDirectory(qa);
File.WriteAllBytes(Path.Combine(qa, "acuse.pdf"), pdf);
File.WriteAllBytes(Path.Combine(qa, "cniedt.xlsx"), ExportacionesFese.Cniedt(datos, cargas, entidades, 2026));
var categoriasAcuse = new[] { "MÉDICO", "PROTECCIÓN CIVIL", "SEGURIDAD", "SERVICIOS PÚBLICOS", "ASISTENCIA", "OTROS SERVICIOS", "IMPROCEDENTES" };
var catalogoAcuse = categoriasAcuse.Select((c, i) => new IncidenteFese { Codigo = (i + 1) * 10000, Categoria = c, Procedente = i < 6 }).ToList();
var filasAcuse = catalogoAcuse.Select((c, i) => new FilaFese("Centro de prueba", c.Codigo, 1000 + i * 100)).ToList();
var previasAcuse = catalogoAcuse.Select((c, i) => new DatoFese { Codigo = c.Codigo, Categoria = c.Categoria, Total = 2000 + i * 100 }).ToList();
var cargaAcuse = new CargaFese { IdCarga = Guid.NewGuid(), Entidad = "Entidad de prueba", Periodo = new(2026, 10, 1), Tipo = "INICIAL", Usuario = "prueba", FechaCreacion = new(2026, 11, 4, 10, 0, 0, TimeSpan.FromHours(-6)) };
var definitivo = ExportacionesFese.Acuse(cargaAcuse, filasAcuse, previasAcuse, catalogoAcuse, true, cargaAcuse.FechaCreacion);
Assert(System.Text.Encoding.Latin1.GetString(definitivo).Contains("certifica") && !System.Text.Encoding.Latin1.GetString(definitivo).Contains("(PREVIO) Tj"), "Solo el definitivo certifica la entrega");
Assert(System.Text.Encoding.Latin1.GetString(definitivo).Contains("0 0 1 rg"), "El comprobante resalta variaciones significativas en azul");
File.WriteAllBytes(Path.Combine(qa, "comprobante.pdf"), definitivo);
cargaAcuse.Advertencias = System.Text.Json.JsonSerializer.Serialize(Enumerable.Range(1, 80).Select(i => $"Centro de prueba {i}: revise la variación reportada respecto del mes anterior."));
var conAdvertencias = ExportacionesFese.Acuse(cargaAcuse, filasAcuse, [], catalogoAcuse, false);
var textoConAdvertencias = System.Text.Encoding.Latin1.GetString(conAdvertencias);
Assert(!textoConAdvertencias.Contains("Observaciones:") && !textoConAdvertencias.Contains("Centro de prueba 1: revise"), "El acuse omite las advertencias de validación");
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
var operacionAbierta = new PermisoFese(true, true);
var operacionCerrada = new PermisoFese(false, false);
Assert(PermisosFese.Combinar(true, operacionAbierta, new(false, true)) == new PermisoFese(false, true), "Carga y actualización se autorizan por separado");
Assert(PermisosFese.Combinar(true, operacionCerrada, operacionAbierta) == operacionCerrada, "El cierre general prevalece sobre el permiso individual");
Assert(PermisosFese.Combinar(false, operacionAbierta, operacionAbierta) == operacionCerrada, "Consulta no obtiene permisos de carga aunque estén activados");
Assert(PermisosFese.Combinar(true, operacionAbierta, null) == operacionAbierta, "Las cuentas existentes conservan permisos hasta su configuración");
Assert(PasswordFese.DebeCambiar(false, true), "Producción exige cambio cuando la cuenta está marcada");
Assert(!PasswordFese.DebeCambiar(true, true), "Desarrollo omite el cambio obligatorio aunque exista marca");
Assert(!PasswordFese.DebeCambiar(false, false), "No se obliga a cambiar a cuentas sin marca");
var hashInicialFese = "$2a$12$Mi2bNvZuKFkh/J8.GbmsP.THgHfMgAdXIujZiQ7rJJkBVy5lfAdk.";
Assert(BCrypt.Net.BCrypt.Verify("password01", hashInicialFese), "El hash del query autentica con password01");
var reglasPassword = new IngestaDeDatos.Api.Configuration.ReglasDdcpOptions { PasswordMinimo = 10, PasswordMaximoBytes = 72 };
Assert(PasswordFese.ValidarNueva("password01", hashInicialFese, reglasPassword) is not null, "No permite conservar la contrasena inicial al cambiarla");
Assert(PasswordFese.ValidarNueva("NuevaClave2026!", hashInicialFese, reglasPassword) is null, "Acepta una nueva contrasena distinta que cumple las reglas");
Assert(PasswordFese.ValidarNueva("corta", hashInicialFese, reglasPassword) is not null, "El cambio respeta las reglas de longitud");
Console.WriteLine("Pruebas correctas");

static void Rechaza(Action accion, string mensaje)
{
    try { accion(); }
    catch (ArgumentException) { Console.WriteLine("OK: " + mensaje); return; }
    throw new Exception(mensaje);
}

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
