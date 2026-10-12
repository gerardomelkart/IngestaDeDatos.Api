# Ingesta de Datos API

API ASP.NET Core (`net10.0`) para DDCP, preparada para incorporar otros módulos.

## Organización

- `IngestaDeDatos.Api/Program.cs`: arranque, inyección de dependencias, JWT, middleware y endpoints.
- `Modules/Autenticacion`: login y su contrato de entrada.
- `Modules/Usuarios`: administración de cuentas, permisos, alcance y contratos de usuarios.
- `Modules/Ddcp/Controllers`: endpoints de entidades, consulta, descarga y cargas.
- `Modules/Ddcp/Models`: entidades, registros, cargas y vista previa.
- `Modules/Ddcp/Services/DdcpService.Consulta.cs`: consulta y exportación de datos confirmados.
- `Modules/Ddcp/Services/DdcpService.ValidacionExcel.cs`: selección de tabla, lectura y validación del archivo, creación de vista previa.
- `Modules/Ddcp/Services/DdcpService.Confirmacion.cs`: confirmación transaccional, histórico y cancelación.
- `Modules/Ddcp/Services/DdcpService.Plantilla.cs`: plantilla mensual.
- `Modules/Ddcp/Services/ExportacionDdcp.cs`: generación del Excel de consulta.
- `Modules/Ddcp/Queries/DdcpSql.cs`: sentencias SQL parametrizadas del módulo.
- `Infrastructure/Data`: conexiones a autenticación y bases de módulos.
- `Infrastructure/Logging`: logs de archivo y registro de solicitudes.

Los archivos parciales forman un único `DdcpService`; conservan la misma instancia y dependencias. Los namespaces públicos se mantienen para conservar las categorías actuales de logs. Las sentencias SQL se extraen como constantes compiladas: no se leen archivos SQL adicionales durante la ejecución.

## Compilar y ejecutar

```powershell
dotnet build .\IngestaDeDatos.Api\IngestaDeDatos.Api.csproj
dotnet run --project .\IngestaDeDatos.Api\IngestaDeDatos.Api.csproj
```

La configuración local continúa en user-secrets o variables de entorno; la productiva se conserva en `/etc/ingesta-api/appsettings.Production.json`. El proyecto, la solución y el ejecutable mantienen sus nombres y rutas para que `DDCP.Front/publicacion/publicar-ddcp.ps1` siga funcionando.

## Reglas de operación

CONSULTA puede consultar y descargar; la carga exige un perfil autorizado. El alcance se vuelve a verificar en el servidor. Validar crea una vista previa; confirmar verifica que no hayan cambiado los datos y guarda el histórico antes de reemplazar cantidades existentes. Una carga repetida no suma de nuevo las cantidades.

La reorganización conserva los endpoints, contratos JSON, consultas, transacciones y configuración. No requiere cambios de BD ni ejecuta scripts sobre desarrollo o producción.
