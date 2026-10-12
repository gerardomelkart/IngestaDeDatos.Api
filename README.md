# Ingesta de Datos API

API ASP.NET Core (`net10.0`) para DDCP, preparada para incorporar otros módulos.

## Organización, siguiendo SIIID

Dentro de `IngestaDeDatos.Api`:

- `Program.cs`: arranque, inyección de dependencias, JWT, middleware y endpoints.
- `Controllers`: endpoints de autenticación, usuarios y DDCP.
- `Services`: coordinación de usuarios, carga, consulta, plantilla y exportaciones.
- `Repositories`: acceso SQL, vista previa, confirmación transaccional, histórico y cancelación, con interfaces para separar la persistencia de los servicios.
- `Repositories/DdcpSql.cs`: sentencias parametrizadas del módulo, como constantes compiladas.
- `Models`: contratos de entrada y modelos de usuarios, entidades, registros y cargas.
- `Readers/DdcpArchivoReader.cs`: selección de tabla, lectura del Excel y normalización de nombres.
- `Validators`: validaciones de periodo y de los datos del usuario.
- `Data/DbFactory.cs`: conexiones a autenticación y bases de módulos.
- `Logging`: logs de archivo y registro de solicitudes.

La estructura de capas sigue el patrón de SIIID. Los namespaces de controladores, modelos y logs se conservan. Las consultas y las transacciones mantienen sus sentencias y parámetros; no se leen archivos SQL adicionales durante la ejecución.

## Compilar y ejecutar

```powershell
dotnet build .\IngestaDeDatos.Api\IngestaDeDatos.Api.csproj
dotnet run --project .\IngestaDeDatos.Api\IngestaDeDatos.Api.csproj
```

La configuración local continúa en user-secrets o variables de entorno; la productiva se conserva en `/etc/ingesta-api/appsettings.Production.json`. El proyecto, la solución y el ejecutable mantienen sus nombres y rutas para que `DDCP.Front/publicacion/publicar-ddcp.ps1` siga funcionando.

## Reglas de operación

CONSULTA puede consultar y descargar; la carga exige un perfil autorizado. El alcance se vuelve a verificar en el servidor. Validar crea una vista previa; confirmar verifica que no hayan cambiado los datos y guarda el histórico antes de reemplazar cantidades existentes. Una carga repetida no suma de nuevo las cantidades.

La reorganización conserva los endpoints, contratos JSON, consultas, transacciones y configuración. No requiere cambios de BD ni ejecuta scripts sobre desarrollo o producción.
