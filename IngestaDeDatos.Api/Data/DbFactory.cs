using Microsoft.Data.SqlClient;

namespace IngestaDeDatos.Api.Data;

public sealed class DbFactory(IConfiguration configuration)
{
    public SqlConnection Auth() => Crear("Autenticacion");
    public SqlConnection Modulo(string modulo)
    {
        var nombre = configuration[$"Modulos:{modulo}:Conexion"] ?? throw new InvalidOperationException("Módulo sin conexión configurada.");
        return Crear(nombre);
    }
    private SqlConnection Crear(string nombre) => new(configuration.GetConnectionString(nombre) ?? throw new InvalidOperationException($"Falta la conexión {nombre}."));
}
