using Dapper;
using IngestaDeDatos.Api.Data;
using System.Data;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace IngestaDeDatos.Api.Fese;

public sealed class RepositorioFese(DbFactory factory)
{
    private const string ColumnasCarga = "c.ID_CARGA IdCarga, c.ID_ENTIDAD IdEntidad, e.NOMBRE Entidad, c.PERIODO Periodo, c.ID_USUARIO IdUsuario, c.USUARIO Usuario, c.ESTADO Estado, c.TIPO Tipo, c.ID_CARGA_BASE IdCargaBase, c.NOMBRE_ARCHIVO NombreArchivo, c.FECHA_CREACION FechaCreacion, c.FECHA_ACEPTACION FechaAceptacion, c.ADVERTENCIAS Advertencias, c.COMPARATIVO_JSON ComparativoJson";
    private const string DesdeCarga = "FROM dbo.FESE_CARGA c JOIN dbo.FESE_ENTIDAD e ON e.ID_ENTIDAD = c.ID_ENTIDAD";
    public static DateTimeOffset Ahora() => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTimeOffset.UtcNow, "America/Mexico_City");

    public async Task<List<EntidadFese>> Entidades()
    {
        await using var db = factory.Modulo("FESE");
        return (await db.QueryAsync<EntidadFese>("SELECT ID_ENTIDAD IdEntidad, NOMBRE Nombre FROM dbo.FESE_ENTIDAD WHERE HABILITADA = 1 ORDER BY NOMBRE")).ToList();
    }
    public async Task<List<IncidenteFese>> Catalogo()
    {
        await using var db = factory.Modulo("FESE");
        return (await db.QueryAsync<IncidenteFese>("""
            SELECT i.CODIGO Codigo, i.NOMBRE Nombre, c.NOMBRE Categoria, c.PROCEDENTE Procedente
            FROM dbo.FESE_INCIDENTE i JOIN dbo.FESE_CATEGORIA c ON c.ID_CATEGORIA = i.ID_CATEGORIA
            WHERE i.HABILITADO = 1 ORDER BY i.CODIGO
            """)).ToList();
    }
    public async Task<CargaFese> Carga(Guid id)
    {
        await using var db = factory.Modulo("FESE");
        return await db.QuerySingleOrDefaultAsync<CargaFese>($"SELECT {ColumnasCarga} {DesdeCarga} WHERE c.ID_CARGA = @id", new { id })
            ?? throw new ArgumentException("Carga inexistente.");
    }
    public async Task<List<CargaFese>> Cargas(int anio, int? entidad)
    {
        await using var db = factory.Modulo("FESE");
        return (await db.QueryAsync<CargaFese>($"SELECT {ColumnasCarga} {DesdeCarga} WHERE c.PERIODO >= @desde AND c.PERIODO < @hasta AND (@entidad IS NULL OR c.ID_ENTIDAD = @entidad) ORDER BY c.PERIODO DESC, e.NOMBRE, c.FECHA_CREACION DESC", new { desde = new DateTime(anio, 1, 1), hasta = new DateTime(anio + 1, 1, 1), entidad })).ToList();
    }
    public async Task<List<DatoFese>> Datos(int anio, int? entidad, int? mes = null)
    {
        await using var db = factory.Modulo("FESE");
        return (await db.QueryAsync<DatoFese>("""
            SELECT ID_ENTIDAD IdEntidad, ENTIDAD Entidad, PERIODO Periodo, CENTRO Centro, CODIGO Codigo,
                   INCIDENTE Incidente, CATEGORIA Categoria, PROCEDENTE Procedente, TOTAL Total
            FROM dbo.FESE_VW_DATOS
            WHERE PERIODO >= @desde AND PERIODO < @hasta AND (@entidad IS NULL OR ID_ENTIDAD = @entidad)
              AND (@mes IS NULL OR MONTH(PERIODO) = @mes)
            ORDER BY ENTIDAD, CENTRO, CODIGO, PERIODO
            """, new { desde = new DateTime(anio, 1, 1), hasta = new DateTime(anio + 1, 1, 1), entidad, mes }, commandTimeout: 180)).ToList();
    }
    public async Task<(List<DatoFese> Datos, List<CargaFese> Cargas, List<EntidadFese> Entidades)> Nacional(int anio)
    {
        await using var db = factory.Modulo("FESE");
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        var parametros = new { desde = new DateTime(anio, 1, 1), hasta = new DateTime(anio + 1, 1, 1) };
        var datos = (await db.QueryAsync<DatoFese>("SELECT ID_ENTIDAD IdEntidad, ENTIDAD Entidad, PERIODO Periodo, CENTRO Centro, CODIGO Codigo, INCIDENTE Incidente, CATEGORIA Categoria, PROCEDENTE Procedente, TOTAL Total FROM dbo.FESE_VW_DATOS WHERE PERIODO >= @desde AND PERIODO < @hasta", parametros, tx, commandTimeout: 180)).ToList();
        var cargas = (await db.QueryAsync<CargaFese>($"SELECT {ColumnasCarga} {DesdeCarga} WHERE c.PERIODO >= @desde AND c.PERIODO < @hasta AND c.ESTADO = 'ACEPTADA'", parametros, tx)).ToList();
        // Las 32 entidades se muestran incluso si una está deshabilitada para nuevas cargas.
        var entidades = (await db.QueryAsync<EntidadFese>("SELECT ID_ENTIDAD IdEntidad, NOMBRE Nombre FROM dbo.FESE_ENTIDAD ORDER BY ID_ENTIDAD", transaction: tx)).ToList();
        await tx.CommitAsync();
        return (datos, cargas, entidades);
    }
    public async Task<List<FilaFese>> Filas(CargaFese carga)
    {
        await using var db = factory.Modulo("FESE");
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        var estado = await db.QuerySingleAsync<string>("SELECT ESTADO FROM dbo.FESE_CARGA WITH (HOLDLOCK) WHERE ID_CARGA=@id", new { id = carga.IdCarga }, tx);
        var sql = estado switch
        {
            "PENDIENTE" => "SELECT CENTRO Centro, CODIGO Codigo, TOTAL Total FROM dbo.FESE_TEMPORAL WHERE ID_CARGA = @id",
            "ACEPTADA" => "SELECT CENTRO Centro, CODIGO Codigo, TOTAL Total FROM dbo.FESE_DATO WHERE ID_CARGA = @id",
            _ => "SELECT CENTRO Centro, CODIGO Codigo, TOTAL Total FROM dbo.FESE_HISTORICO WHERE ID_CARGA = @id"
        };
        var filas = (await db.QueryAsync<FilaFese>(sql, new { id = carga.IdCarga }, tx)).ToList();
        await tx.CommitAsync();
        return filas;
    }
    public async Task<Guid?> Guardar(int entidad, DateTime periodo, int usuario, string nombreUsuario, string nombreArchivo, byte[] archivo, LecturaFese lectura, bool actualizacion)
    {
        await using var db = factory.Modulo("FESE");
        await db.OpenAsync();
        await using var tx = await db.BeginTransactionAsync(IsolationLevel.Serializable);
        if (!await db.ExecuteScalarAsync<bool>("SELECT HABILITADA FROM dbo.FESE_ENTIDAD WHERE ID_ENTIDAD = @entidad", new { entidad }, tx))
            throw new ArgumentException("La entidad está deshabilitada.");
        var cargaBase = await db.QuerySingleOrDefaultAsync<Guid?>("SELECT ID_CARGA FROM dbo.FESE_CARGA WHERE ID_ENTIDAD = @entidad AND PERIODO = @periodo AND ESTADO = 'ACEPTADA'", new { entidad, periodo }, tx);
        // Es un conflicto de operación esperado. No se crea otra carga inicial.
        // La comprobación permanece dentro de la transacción para evitar carreras.
        if (!actualizacion && cargaBase.HasValue) return null;
        PoliticaEntregaFese.Validar(periodo, actualizacion, cargaBase.HasValue, Ahora().Date);
        var anterior = periodo.AddMonths(-1);
        var comparativo = (await db.QueryAsync<DatoFese>("SELECT ID_ENTIDAD IdEntidad, ENTIDAD Entidad, PERIODO Periodo, CENTRO Centro, CODIGO Codigo, INCIDENTE Incidente, CATEGORIA Categoria, PROCEDENTE Procedente, TOTAL Total FROM dbo.FESE_VW_DATOS WHERE ID_ENTIDAD = @entidad AND PERIODO = @anterior", new { entidad, anterior }, tx)).ToList();
        if (comparativo.Count > 0)
        {
            var categorias = (await db.QueryAsync<IncidenteFese>("SELECT i.CODIGO Codigo, c.NOMBRE Categoria FROM dbo.FESE_INCIDENTE i JOIN dbo.FESE_CATEGORIA c ON c.ID_CATEGORIA=i.ID_CATEGORIA", transaction: tx)).ToDictionary(x => x.Codigo, x => x.Categoria);
            foreach (var categoria in categorias.Values.Distinct())
            {
                var actual = lectura.Filas.Where(x => categorias[x.Codigo] == categoria).Sum(x => x.Total);
                var previo = comparativo.Where(x => x.Categoria == categoria).Sum(x => x.Total);
                if (previo == 0) continue;
                var variacion = ((decimal)actual - previo) * 100 / previo;
                if (Math.Abs(variacion) >= 10)
                    lectura.Advertencias.Add($"{categoria}: variación de {variacion:0.00}% respecto del mes anterior. Revise el comparativo del acuse.");
            }
        }
        var comparativoJson = JsonSerializer.Serialize(comparativo);
        var id = Guid.NewGuid();
        var fecha = Ahora();
        await db.ExecuteAsync("""
            INSERT dbo.FESE_CARGA(ID_CARGA, ID_ENTIDAD, PERIODO, ID_USUARIO, USUARIO, TIPO, ESTADO,
                ID_CARGA_BASE, NOMBRE_ARCHIVO, ARCHIVO, SHA256, ADVERTENCIAS, FECHA_CREACION, COMPARATIVO_JSON)
            VALUES (@id, @entidad, @periodo, @usuario, @nombreUsuario, @tipo, 'PENDIENTE', @cargaBase,
                @nombreArchivo, @archivo, @hash, @advertencias, @fecha, @comparativoJson)
            """, new { id, entidad, periodo, usuario, nombreUsuario, tipo = cargaBase.HasValue ? "ACTUALIZACION" : "INICIAL", cargaBase, nombreArchivo, archivo, hash = Convert.ToHexString(SHA256.HashData(archivo)), advertencias = JsonSerializer.Serialize(lectura.Advertencias), fecha, comparativoJson }, tx);
        var tabla = new DataTable();
        tabla.Columns.Add("ID_CARGA", typeof(Guid));
        tabla.Columns.Add("CENTRO", typeof(string));
        tabla.Columns.Add("CODIGO", typeof(int));
        tabla.Columns.Add("TOTAL", typeof(long));
        foreach (var f in lectura.Filas) tabla.Rows.Add(id, f.Centro, f.Codigo, f.Total);
        using var bulk = new SqlBulkCopy(db, SqlBulkCopyOptions.CheckConstraints, (SqlTransaction)tx);
        bulk.DestinationTableName = "dbo.FESE_TEMPORAL";
        foreach (DataColumn c in tabla.Columns) bulk.ColumnMappings.Add(c.ColumnName, c.ColumnName);
        await bulk.WriteToServerAsync(tabla);
        await db.ExecuteAsync("INSERT dbo.FESE_BITACORA(ID_CARGA, ID_USUARIO, EVENTO, FECHA, DETALLE) VALUES (@id, @usuario, 'VALIDACION', @fecha, N'Archivo validado; pendiente de revisión del acuse.')", new { id, usuario, fecha }, tx);
        await tx.CommitAsync();
        return id;
    }
    public async Task Confirmar(Guid id, int usuario, byte[] pdf, string comparativo)
    {
        await using var db = factory.Modulo("FESE");
        await db.ExecuteAsync("dbo.FESE_ACEPTAR", new { IdCarga = id, IdUsuario = usuario, AcusePdf = pdf, ComparativoJson = comparativo }, commandType: CommandType.StoredProcedure, commandTimeout: 180);
    }
    public async Task Cancelar(Guid id, int usuario)
    {
        await using var db = factory.Modulo("FESE");
        await db.ExecuteAsync("dbo.FESE_CANCELAR", new { IdCarga = id, IdUsuario = usuario }, commandType: CommandType.StoredProcedure);
    }
    public async Task<byte[]> Archivo(Guid id, bool acuse)
    {
        await using var db = factory.Modulo("FESE");
        return await db.QuerySingleOrDefaultAsync<byte[]>($"SELECT {(acuse ? "ACUSE_PDF" : "ARCHIVO")} FROM dbo.FESE_CARGA WHERE ID_CARGA = @id", new { id })
            ?? throw new ArgumentException("El archivo todavía no está disponible.");
    }
}
