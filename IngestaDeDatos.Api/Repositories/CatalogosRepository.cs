using Dapper;
using IngestaDeDatos.Api.Data;
using IngestaDeDatos.Api.Models;

namespace IngestaDeDatos.Api.Repositories;

public sealed class CatalogosRepository(DbFactory factory, IConfiguration configuration) : ICatalogosRepository
{
    public string Modulo => configuration["Sistema:Modulo"] ?? throw new InvalidOperationException("Falta Sistema:Modulo.");
    public const string RolColumnas = "CLAVE Clave, NOMBRE Nombre, PUEDE_CARGAR PuedeCargar, PUEDE_ADMINISTRAR_USUARIOS PuedeAdministrarUsuarios, NACIONAL_OBLIGATORIO NacionalObligatorio, PREDETERMINADO Predeterminado";
    public async Task<List<RolCatalogo>> Roles()
    {
        await using var db = factory.Auth();
        return (await db.QueryAsync<RolCatalogo>($"SELECT {RolColumnas} FROM dbo.CAT_ROL WHERE HABILITADO = 1 ORDER BY ORDEN, NOMBRE;")).ToList();
    }
    public async Task<List<MesCatalogo>> Meses()
    {
        await using var db = factory.Modulo(Modulo);
        var meses = (await db.QueryAsync<MesCatalogo>("SELECT ID_MES IdMes, NOMBRE Nombre FROM dbo.CAT_MES ORDER BY ID_MES;")).ToList();
        if (!meses.Select(x => x.IdMes).SequenceEqual(Enumerable.Range(1, 12)) || meses.Any(x => string.IsNullOrWhiteSpace(x.Nombre)))
            throw new InvalidOperationException("El catálogo CAT_MES debe contener los doce meses del calendario.");
        return meses;
    }
    public async Task<string> NombreModulo()
    {
        await using var db = factory.Auth();
        return await db.QuerySingleAsync<string>("SELECT NOMBRE FROM dbo.ING_MODULO WHERE CLAVE = @modulo AND HABILITADO = 1;", new { modulo = Modulo });
    }
}
