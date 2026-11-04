using Microsoft.Data.SqlClient;

namespace IngestaDeDatos.Api.Data;

public sealed class DbFactory(IConfiguration configuration, IHostEnvironment environment)
{
    public SqlConnection Auth() => Crear("Autenticacion");

    public SqlConnection Modulo(string modulo)
    {
        var nombre = configuration[$"Modulos:{modulo}:Conexion"]
            ?? throw new InvalidOperationException("Módulo sin conexión configurada.");
        var conexion = Crear(nombre);
        if (modulo == "FESE")
        {
            var esperada = environment.IsDevelopment() ? "FESE_DEV" : "FESE";
            var cadena = new SqlConnectionStringBuilder(conexion.ConnectionString);
            if (!string.Equals(cadena.InitialCatalog, esperada, StringComparison.OrdinalIgnoreCase))
            {
                conexion.Dispose();
                throw new InvalidOperationException($"La conexión FESE no corresponde al ambiente. Se esperaba {esperada}.");
            }
        }
        return conexion;
    }

    private SqlConnection Crear(string nombre)
    {
        var cadena = configuration.GetConnectionString(nombre);
        if (string.IsNullOrWhiteSpace(cadena))
            throw new InvalidOperationException($"Falta la conexión {nombre}.");
        return new SqlConnection(cadena);
    }
}
