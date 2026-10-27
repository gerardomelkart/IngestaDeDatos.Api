# DDCP · API de ingesta y consulta

DDCP es una aplicación web para recibir, validar y consultar reportes mensuales de **dispositivos decomisados en centros penitenciarios** y personas puestas a disposición. Permite trabajar con alcance nacional o por entidad, según los permisos de la cuenta.

## Repositorios del proyecto

La solución se mantiene en tres repositorios complementarios:

| Repositorio | Responsabilidad | Tecnologías |
| --- | --- | --- |
| [DDCP.Front](https://github.com/gerardomelkart/DDCP.Front) | Interfaz, navegación, consulta y carga de archivos. | Angular, TypeScript, HTML y CSS |
| [IngestaDeDatos.Api](https://github.com/gerardomelkart/IngestaDeDatos.Api) | Autenticación, autorización, validación, confirmación de cargas y exportación. | ASP.NET Core, C#, Dapper y ClosedXML |
| [DDCP.BD](https://github.com/gerardomelkart/DDCP.BD) | Estructura SQL, catálogos, historial y scripts de mantenimiento. | SQL Server y T-SQL |

## Cómo se conectan

El navegador ejecuta el Front, que consume la API mediante HTTP y JSON. La API valida la sesión y el alcance del usuario antes de leer o modificar SQL Server. **El Front no se conecta directamente a la base de datos.**

Los catálogos de entidades, roles, permisos y meses se consultan desde la base a través de la API. Las conexiones y claves se configuran por ambiente, fuera del código versionado.

## Flujo de trabajo

1. El usuario inicia sesión y recibe las opciones disponibles para su rol y entidad.
2. Selecciona el periodo y carga un archivo Excel; el nombre de la hoja no determina su validez.
3. La API valida estructura, entidades y cantidades y devuelve una vista previa.
4. El usuario revisa y confirma. Si el periodo ya tenía información, se reemplazan las cantidades afectadas y se conserva su histórico.
5. Los datos confirmados se consultan por periodo y entidad y se pueden exportar a Excel.

## Desarrollo

Proyecto desarrollado por **[Gerardo Melkart Noeller Álvarez](https://github.com/gerardomelkart)**. Integra desarrollo de interfaz, servicios de backend, modelado de datos, procesamiento de Excel y automatización de publicación en Linux.

## Qué contiene este repositorio

- Autenticación con JWT y contraseñas protegidas con BCrypt.
- Autorización por permisos vigentes y alcance nacional o por entidad.
- Lectura y validación de Excel, generación de plantillas y exportación de consultas.
- Confirmación de cargas con histórico y control de cambios posteriores a la vista previa.
- Consulta de catálogos y reglas no secretas para el Front.
- Registro de solicitudes, inicios de sesión y errores.
- Separación por controladores, servicios, repositorios, modelos, lectores y validadores.

## Ejecutar la solución

Se necesita .NET 10 y acceso a SQL Server con el esquema y los permisos descritos en [DDCP.BD](https://github.com/gerardomelkart/DDCP.BD#readme). Configurar las conexiones y la clave JWT con secretos de usuario o variables de entorno siguiendo las instrucciones de este README. Después iniciar la API y configurar [DDCP.Front](https://github.com/gerardomelkart/DDCP.Front#readme) para consumirla.

## Documentación técnica

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

## Consulta y exportación 1.0

`GET api/ddcp/datos/excel` admite los parámetros opcionales `filtroAnio`, `filtroMes`, `filtroEntidad`, `filtroDispositivos`, `filtroPersonas`, `orden` y `ascendente`, además del periodo y entidad existentes. La búsqueda es parcial y sin distinguir acentos; el mes usa su nombre del catálogo. El orden admite `anio`, `mes`, `entidad`, `dispositivos` o `personas`. Primero se obtiene la información dentro del alcance autorizado y después se aplican los filtros. Las cantidades y los totales mantienen su precisión. No requiere migración de la base.
