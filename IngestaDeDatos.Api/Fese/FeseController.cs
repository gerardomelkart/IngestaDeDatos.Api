using Dapper;
using IngestaDeDatos.Api.Configuration;
using IngestaDeDatos.Api.Data;
using IngestaDeDatos.Api.Models;
using IngestaDeDatos.Api.Services;
using IngestaDeDatos.Api.Validators;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Data;
using System.Text.Json;

namespace IngestaDeDatos.Api.Fese;

public sealed class ErrorFeseAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        var error = context.Exception;
        var status = error is UnauthorizedAccessException ? 403 : error is ArgumentException ? 400 : error is SqlException sql && sql.Number is >= 51000 and <= 51999 ? 409 : 500;
        var mensaje = status == 500 ? "No se pudo completar la operación FESE. Consulte el registro del servicio." : error.Message;
        if (status == 500)
            context.HttpContext.RequestServices.GetRequiredService<ILogger<ErrorFeseAttribute>>()
                .LogError("Error FESE {Tipo}; solicitud {Solicitud}.", error.GetType().Name, context.HttpContext.TraceIdentifier);
        context.Result = new ObjectResult(new { mensaje }) { StatusCode = status };
        context.ExceptionHandled = true;
    }
}

[ApiController, Authorize, Route("api/fese"), ErrorFese]
public sealed class FeseController(AccesoFese acceso, RepositorioFese repo, DbFactory factory, LectorFese lector, UsuariosService usuarios, IOptions<ReglasDdcpOptions> reglas) : ControllerBase
{
    private const string MimeExcel = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
    private static void Anio(int anio)
    {
        if (anio is < 2000 or > 2100) throw new ArgumentException("El año debe estar entre 2000 y 2100.");
    }
    private async Task<CargaFese> Autorizada(Guid id, bool cargar = false)
    {
        var usuario = await acceso.Actual(User, cargar);
        var carga = await repo.Carga(id);
        AccesoFese.Entidad(usuario, carga.IdEntidad);
        return carga;
    }
    [HttpGet("catalogos")]
    public async Task<IActionResult> Catalogos()
    {
        var usuario = await acceso.Actual(User);
        await using var db = factory.Auth();
        var roles = await db.QueryAsync("SELECT CLAVE clave, NOMBRE nombre, NACIONAL_OBLIGATORIO nacionalObligatorio FROM dbo.CAT_ROL WHERE HABILITADO = 1 ORDER BY ORDEN");
        var meses = await db.QueryAsync("SELECT ID_MES id, NOMBRE nombre FROM dbo.CAT_MES ORDER BY ID_MES");
        var corte = PoliticaEntregaFese.Corte(RepositorioFese.Ahora().Date);
        return Ok(new { corte = new { anio = corte.Year, mes = corte.Month }, usuario, entidades = (await repo.Entidades()).Where(e => usuario.EsNacional || e.IdEntidad == usuario.IdEntidad), incidentes = await repo.Catalogo(), roles, meses });
    }
    [HttpPost("cargas/validar")]
    public async Task<IActionResult> Validar([FromForm] IFormFile archivo, [FromForm] int idEntidad, [FromForm] int anio, [FromForm] int mes, [FromForm] bool actualizacion = false)
    {
        var usuario = await acceso.Actual(User, true);
        var entidad = AccesoFese.Entidad(usuario, idEntidad);
        Anio(anio);
        if (mes is < 1 or > 12) throw new ArgumentException("Seleccione un mes válido.");
        var periodo = new DateTime(anio, mes, 1);
        if (periodo > PoliticaEntregaFese.Corte(RepositorioFese.Ahora().Date)) throw new ArgumentException("No se permite reportar periodos futuros.");
        if (!actualizacion) PoliticaEntregaFese.Validar(periodo, false, false, RepositorioFese.Ahora().Date);
        if (archivo.Length <= 0 || archivo.Length > reglas.Value.MaxArchivoBytes) throw new ArgumentException("Archivo vacío o demasiado grande.");
        if (Path.GetExtension(archivo.FileName).ToLowerInvariant() is not (".xlsx" or ".xlsm"))
            throw new ArgumentException("Seleccione un archivo .xlsx o .xlsm. Para .xls, guarde una copia como .xlsx.");
        using var stream = new MemoryStream();
        await archivo.CopyToAsync(stream);
        var bytes = stream.ToArray();
        var catalogo = await repo.Catalogo();
        var entidadNombre = (await repo.Entidades()).SingleOrDefault(e => e.IdEntidad == entidad)?.Nombre
            ?? throw new ArgumentException("La entidad está deshabilitada.");
        LecturaFese lectura;
        try { lectura = lector.Leer(bytes, catalogo, entidadNombre, periodo); }
        catch (Exception ex) when (ex is not SqlException)
        {
            throw new ArgumentException("No fue posible leer el libro de Excel. Revise que sea válido y no esté protegido.");
        }
        if (lectura.Errores.Count > 0) return BadRequest(new { mensaje = "Revise la información del archivo.", errores = lectura.Errores, advertencias = lectura.Advertencias });
        await using (var db = factory.Modulo("FESE"))
        {
            var previos = (await db.QueryAsync<DatoFese>("SELECT PERIODO Periodo, CENTRO Centro, CODIGO Codigo, TOTAL Total FROM dbo.FESE_DATO WHERE ID_ENTIDAD = @entidad AND PERIODO < @periodo", new { entidad, periodo }, commandTimeout: 180)).ToList();
            foreach (var centro in lectura.Filas.GroupBy(x => LectorFese.Normalizar(x.Centro)))
            {
                var anteriores = previos.Where(x => LectorFese.Normalizar(x.Centro) == centro.Key).ToList();
                if (anteriores.Count == 0) lectura.Advertencias.Add($"Centro nuevo o sin histórico: {centro.First().Centro}.");
                foreach (var grupo in anteriores.GroupBy(x => x.Periodo))
                {
                    var orden = centro.OrderBy(x => x.Codigo).Select(x => (x.Codigo, x.Total));
                    if (orden.SequenceEqual(grupo.OrderBy(x => x.Codigo).Select(x => (x.Codigo, x.Total))))
                        lectura.Advertencias.Add($"{centro.First().Centro}: distribución idéntica a {grupo.Key:yyyy-MM}; confirme que corresponde al periodo seleccionado.");
                }
            }
        }
        var actuales = await repo.Datos(anio, entidad, mes);
        var recibidos = lectura.Filas.Select(x => LectorFese.Normalizar(x.Centro)).ToHashSet();
        foreach (var centro in actuales.Select(x => x.Centro).Distinct().Where(x => !recibidos.Contains(LectorFese.Normalizar(x))))
            lectura.Advertencias.Add($"La actualización eliminará el centro {centro} del periodo porque no está en el archivo nuevo. Revise las diferencias antes de aceptar.");
        var nombre = Path.GetFileName(archivo.FileName.Replace('\\', '/'));
        if (nombre.Length > 255) throw new ArgumentException("El nombre del archivo es demasiado largo.");
        var id = await repo.Guardar(entidad, periodo, usuario.IdUsuario, usuario.Usuario, nombre, bytes, lectura, actualizacion);
        return Ok(new { idCarga = id, advertencias = lectura.Advertencias, filas = lectura.Filas.Count, total = lectura.Filas.Sum(x => x.Total) });
    }
    [HttpGet("cargas")]
    public async Task<IActionResult> Cargas(int anio, int? idEntidad)
    {
        Anio(anio);
        var u = await acceso.Actual(User);
        var entidad = u.EsNacional && idEntidad is null ? (int?)null : AccesoFese.Entidad(u, idEntidad);
        return Ok(await repo.Cargas(anio, entidad));
    }
    [HttpGet("cargas/{id:guid}")]
    public async Task<IActionResult> Detalle(Guid id)
    {
        var c = await Autorizada(id);
        var filas = await repo.Filas(c);
        var baseFilas = c.IdCargaBase.HasValue ? await repo.Filas(await repo.Carga(c.IdCargaBase.Value)) : [];
        var antiguos = baseFilas.ToDictionary(x => (LectorFese.Normalizar(x.Centro), x.Codigo));
        var nuevos = filas.ToDictionary(x => (LectorFese.Normalizar(x.Centro), x.Codigo));
        var diferencias = antiguos.Keys.Union(nuevos.Keys).Select(k => new DiferenciaFese(
            nuevos.GetValueOrDefault(k)?.Centro ?? antiguos[k].Centro, k.Codigo,
            antiguos.TryGetValue(k, out var a) ? a.Total : null,
            nuevos.TryGetValue(k, out var n) ? n.Total : null)).Where(x => x.Anterior != x.Nuevo).ToList();
        return Ok(new { carga = c, advertencias = JsonSerializer.Deserialize<List<string>>(c.Advertencias), total = filas.Sum(x => x.Total), centros = filas.Select(x => x.Centro).Distinct().Count(), diferencias });
    }
    [HttpGet("cargas/{id:guid}/diferencias")]
    public async Task<IActionResult> Diferencias(Guid id)
    {
        var c = await Autorizada(id);
        var filas = await repo.Filas(c);
        var anteriores = c.IdCargaBase.HasValue ? await repo.Filas(await repo.Carga(c.IdCargaBase.Value)) : [];
        return File(ExportacionesFese.Diferencias(anteriores, filas), MimeExcel, $"FESE-diferencias-{id}.xlsx");
    }
    private async Task<(byte[] Pdf, List<DatoFese> Comparativo)> PrepararAcuse(CargaFese c, bool aceptado)
    {
        var datos = JsonSerializer.Deserialize<List<DatoFese>>(c.ComparativoJson) ?? [];
        return (ExportacionesFese.Acuse(c, await repo.Filas(c), datos, await repo.Catalogo(), aceptado), datos);
    }
    [HttpGet("cargas/{id:guid}/acuse")]
    public async Task<IActionResult> Acuse(Guid id)
    {
        var c = await Autorizada(id);
        if (c.Estado is "ACEPTADA" or "SUSTITUIDA") return File(await repo.Archivo(id, true), "application/pdf", $"Acuse-FESE-{id}.pdf");
        if (c.Estado != "PENDIENTE") throw new ArgumentException("La carga fue cancelada.");
        var resultado = await PrepararAcuse(c, false);
        return File(resultado.Pdf, "application/pdf");
    }
    [HttpPost("cargas/{id:guid}/aceptar")]
    public async Task<IActionResult> Aceptar(Guid id)
    {
        var c = await Autorizada(id, true);
        var usuario = await acceso.Actual(User, true);
        if (c.Estado == "ACEPTADA") return Ok(new { mensaje = "La entrega ya está aceptada." });
        if (c.Estado != "PENDIENTE") throw new ArgumentException("La carga ya no está pendiente.");
        PoliticaEntregaFese.Validar(c.Periodo, c.Tipo == "ACTUALIZACION", c.IdCargaBase.HasValue, RepositorioFese.Ahora().Date);
        var acuse = await PrepararAcuse(c, true);
        await repo.Confirmar(id, usuario.IdUsuario, acuse.Pdf, JsonSerializer.Serialize(acuse.Comparativo));
        return Ok(new { mensaje = "Entrega aceptada correctamente." });
    }
    [HttpPost("cargas/{id:guid}/cancelar")]
    public async Task<IActionResult> Cancelar(Guid id)
    {
        await Autorizada(id, true);
        var u = await acceso.Actual(User, true);
        await repo.Cancelar(id, u.IdUsuario);
        return Ok(new { mensaje = "Carga cancelada; la información vigente se conserva." });
    }
    [HttpGet("cargas/{id:guid}/original")]
    public async Task<IActionResult> Original(Guid id)
    {
        var c = await Autorizada(id);
        return File(await repo.Archivo(id, false), "application/octet-stream", c.NombreArchivo);
    }
    [HttpGet("exportar")]
    public async Task<IActionResult> Exportar(int anio, int? idEntidad, int? mes, string? categoria, int? codigo, bool anual = false)
    {
        Anio(anio);
        if (mes.HasValue && mes is < 1 or > 12) throw new ArgumentException("Mes inválido.");
        var u = await acceso.Actual(User);
        var entidad = u.EsNacional && idEntidad is null ? (int?)null : AccesoFese.Entidad(u, idEntidad);
        var datos = (await repo.Datos(anio, entidad, mes)).Where(x => (string.IsNullOrEmpty(categoria) || x.Categoria == categoria) && (!codigo.HasValue || x.Codigo == codigo));
        return File(ExportacionesFese.Excel(datos, anual), MimeExcel, $"FESE-{anio}-{(anual ? "anual" : "consulta")}.xlsx");
    }
    [HttpGet("cniedt")]
    public async Task<IActionResult> Cniedt(int anio)
    {
        Anio(anio);
        var u = await acceso.Actual(User);
        if (!u.EsNacional) throw new UnauthorizedAccessException("El formato nacional requiere alcance nacional.");
        var nacional = await repo.Nacional(anio);
        return File(ExportacionesFese.Cniedt(nacional.Datos, nacional.Cargas, nacional.Entidades, anio), MimeExcel, $"Formato-CNIEDT-{anio}.xlsx");
    }
    [HttpGet("usuarios")]
    public async Task<IActionResult> Usuarios()
    {
        await acceso.Actual(User, administra: true);
        await using var db = factory.Auth();
        return Ok(await db.QueryAsync("""
            SELECT u.ID_USUARIO idUsuario, u.USUARIO usuario, u.NOMBRE nombre, u.ROL rol,
                u.ES_NACIONAL esNacional, u.ID_ENTIDAD idEntidad, u.HABILITADO habilitado,
                CAST(CASE WHEN EXISTS(SELECT 1 FROM dbo.ING_USUARIO_MODULO m WHERE m.ID_USUARIO=u.ID_USUARIO AND m.CLAVE='FESE')
                    OR (r.PUEDE_ADMINISTRAR_USUARIOS=1 AND u.ES_NACIONAL=1) THEN 1 ELSE 0 END AS bit) accesoFese
            FROM dbo.ING_USUARIO u JOIN dbo.CAT_ROL r ON r.CLAVE=u.ROL ORDER BY u.USUARIO
            """));
    }
    public sealed record Asignacion(bool Habilitado);
    [HttpPut("usuarios/{id:int}/acceso")]
    public async Task<IActionResult> Asignar(int id, Asignacion request)
    {
        var u = await acceso.Actual(User, administra: true);
        if (id == u.IdUsuario && !request.Habilitado) throw new ArgumentException("No puede retirarse su propio acceso.");
        await using var db = factory.Auth();
        if (!await db.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.ING_USUARIO WHERE ID_USUARIO=@id) THEN 1 ELSE 0 END", new { id })) throw new ArgumentException("Usuario inexistente.");
        await db.ExecuteAsync(request.Habilitado
            ? "INSERT dbo.ING_USUARIO_MODULO(ID_USUARIO,CLAVE) SELECT @id,'FESE' WHERE NOT EXISTS(SELECT 1 FROM dbo.ING_USUARIO_MODULO WHERE ID_USUARIO=@id AND CLAVE='FESE')"
            : "DELETE dbo.ING_USUARIO_MODULO WHERE ID_USUARIO=@id AND CLAVE='FESE'", new { id });
        return Ok(new { mensaje = "Acceso FESE actualizado. El superusuario nacional conserva acceso por su rol." });
    }
    [HttpPost("usuarios")]
    public async Task<IActionResult> CrearUsuario(UsuarioRequest request)
    {
        await acceso.Actual(User, administra: true);
        await using var db = factory.Auth();
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        var rol = await db.QuerySingleOrDefaultAsync<RolCatalogo>("SELECT CLAVE Clave,NOMBRE Nombre,PUEDE_CARGAR PuedeCargar,PUEDE_ADMINISTRAR_USUARIOS PuedeAdministrarUsuarios,NACIONAL_OBLIGATORIO NacionalObligatorio FROM dbo.CAT_ROL WHERE CLAVE=@Rol AND HABILITADO=1", request, tx)
            ?? throw new ArgumentException("Rol inválido.");
        UsuarioValidator.Validar(request, true, reglas.Value, rol);
        if (await db.ExecuteScalarAsync<bool>("SELECT CASE WHEN EXISTS(SELECT 1 FROM dbo.ING_USUARIO WHERE USUARIO=@Usuario) THEN 1 ELSE 0 END", request, tx)) throw new ArgumentException("Ya existe ese usuario; asígnele acceso FESE desde la lista.");
        if (request.IdEntidad.HasValue && !await db.ExecuteScalarAsync<bool>("SELECT HABILITADO FROM dbo.CAT_ENTIDAD WHERE ID_ENTIDAD=@IdEntidad", request, tx)) throw new ArgumentException("Entidad inválida.");
        var id = await db.ExecuteScalarAsync<int>("INSERT dbo.ING_USUARIO(USUARIO,NOMBRE,PASSWORD_HASH,ROL,ES_NACIONAL,ID_ENTIDAD) OUTPUT inserted.ID_USUARIO VALUES(@Usuario,@Nombre,@hash,@Rol,@EsNacional,@IdEntidad)", new { Usuario = request.Usuario.Trim(), Nombre = request.Nombre.Trim(), hash = BCrypt.Net.BCrypt.HashPassword(request.Password, reglas.Value.BCryptWorkFactor), request.Rol, request.EsNacional, request.IdEntidad }, tx);
        await db.ExecuteAsync("INSERT dbo.ING_USUARIO_MODULO(ID_USUARIO,CLAVE) VALUES(@id,'FESE')", new { id }, tx);
        await tx.CommitAsync();
        return Ok(new { idUsuario = id });
    }
    [HttpPut("usuarios/{id:int}")]
    public async Task<IActionResult> EditarUsuario(int id, UsuarioRequest request)
    {
        var u = await acceso.Actual(User, administra: true);
        await usuarios.Editar(id, request, u.IdUsuario);
        return Ok(new { mensaje = "Cuenta central actualizada; el cambio también aplica a sus otros sistemas." });
    }
    [HttpPut("usuarios/{id:int}/estado")]
    public async Task<IActionResult> EstadoUsuario(int id, Asignacion request)
    {
        var u = await acceso.Actual(User, administra: true);
        await usuarios.CambiarEstado(id, request.Habilitado, u.IdUsuario);
        return Ok(new { mensaje = "Estado de la cuenta central actualizado." });
    }
}
