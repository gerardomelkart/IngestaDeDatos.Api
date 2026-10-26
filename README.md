# Ingesta de Datos API

ASP.NET Core (`net10.0`), organizado en `Controllers`, `Services`, `Repositories`, `Models`, `Readers`, `Validators`, `Configuration`, `Data` y `Logging`.

## Fuentes de datos

| Datos | Fuente |
| --- | --- |
| Entidades | `dbo.CAT_ENTIDAD` |
| Roles, nombres, permisos, alcance obligatorio y rol predeterminado | `dbo.CAT_ROL` |
| Meses | `dbo.CAT_MES` |
| Nombre e identificación del módulo | `dbo.ING_MODULO` y `Sistema:Modulo` |
| Años, tamaño de archivo, máximos de captura, contraseñas, paginación | Sección `Reglas` de configuración |
| Conexiones y JWT | user-secrets, variables o archivo externo productivo |
| Logs | Sección `Logs` |

La autorización se resuelve en la API con los permisos actuales del catálogo, no con listas locales ni con las capacidades que envíe el navegador. Se conserva la protección contra deshabilitar la propia cuenta o eliminar el último administrador activo. Los límites configurados se validan contra los tipos de almacenamiento y el formato BCrypt para evitar aceptar una configuración incompatible con el esquema.

`GET /api/sistema` publica solo identificación del módulo. `GET /api/catalogos` exige autenticación y devuelve roles, meses, reglas no secretas y capacidades actuales del usuario. Las entidades conservan el alcance del usuario. El Excel y la selección del mes leen el mismo catálogo de BD que el Front.

## Base de datos

Primera instalación: ejecutar en orden los scripts `estructura/01_adaptar_ddcp.sql`, `02_ingesta.sql` y `03_catalogos.sql` del repositorio DDCP.BD.

En una base existente aplicar únicamente `03_catalogos.sql` antes de publicar esta versión. El script añade los catálogos que faltan y conserva usuarios, cargas e históricos. Los registros iniciales de roles/meses pertenecen a la BD y no se sobrescriben al repetir el script. La aplicación no mantiene un catálogo alternativo en código.

La cuenta SQL necesita SELECT sobre `CAT_ROL` y `CAT_MES`. Si tiene `db_datareader`, incluye estas tablas; si usa permisos por objeto, el administrador debe concederlos explícitamente. Los permisos de carga y de `SEQ_DECOMISO` se conservan.

## Configuración local

`appsettings.json` contiene reglas no secretas y conexiones vacías: no hay conexión implícita a LocalDB ni contraseña de respaldo. Desde la carpeta de este repositorio:

```powershell
dotnet user-secrets set "ConnectionStrings:Autenticacion" "CADENA_DE_CONEXION" --project .\IngestaDeDatos.Api\IngestaDeDatos.Api.csproj
dotnet user-secrets set "ConnectionStrings:DDCP" "CADENA_DE_CONEXION" --project .\IngestaDeDatos.Api\IngestaDeDatos.Api.csproj
dotnet user-secrets set "Jwt:SecretKey" "CLAVE_ALEATORIA_DE_AL_MENOS_32_BYTES" --project .\IngestaDeDatos.Api\IngestaDeDatos.Api.csproj
dotnet run --project .\IngestaDeDatos.Api\IngestaDeDatos.Api.csproj
```

Para el alta inicial se requieren `INGESTA_ADMIN_USUARIO`, `INGESTA_ADMIN_PASSWORD` e `INGESTA_ADMIN_NOMBRE`, y el argumento `--crear-superusuario`. El rol administrador se obtiene del catálogo, sin usuario/contraseña/rol de respaldo en el código. Solo se permite cuando no existen usuarios.

## Configuración productiva

Se conserva el archivo externo configurado en `deploy.env`. La instalación existente usa `/etc/ingesta-api/appsettings.Production.json`. Las cadenas deben coincidir con los datos de `Despliegue`, que sirven para verificarlas antes de publicar. No subir ese archivo a Git ni introducir credenciales en `appsettings.json`.

Las reglas no secretas del paquete pueden sobrescribirse en el archivo externo o con variables de entorno, por ejemplo `Reglas__MaxArchivoBytes`. Después de cambiar configuración se reinicia el servicio; después de cambiar datos de catálogo basta recargar las pantallas para obtener las etiquetas actualizadas. Las autorizaciones del servidor consultan los permisos vigentes en BD en cada operación.

Los perfiles y las instrucciones de publicación se encuentran en `DDCP.Front/publicacion`. Los scripts conservan conexiones, clave JWT y opciones productivas al validar una instalación existente.
