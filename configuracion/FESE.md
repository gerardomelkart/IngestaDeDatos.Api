# Configuración FESE

Mantener las conexiones actuales de autenticación y DDCP. Agregar `ConnectionStrings:FESE`:

| Ambiente | Conexión FESE | Autenticación existente |
|---|---|---|
| Visual Studio / Development | FESE_DEV | DDCP_DEV |
| Servidor / Production | FESE | DDCP |

Visual Studio ya define `ASPNETCORE_ENVIRONMENT=Development`. El código comprueba que la conexión del módulo FESE tenga la base correspondiente; nunca toma DDCP como respaldo de FESE. Compilar Release no selecciona producción: lo hace el ambiente del proceso.

Para desarrollo, desde PowerShell Windows:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\configuracion\configurar-fese-local.ps1"
```

Este script lee el secreto DDCP ya configurado, conserva servidor/usuario/contraseña y cambia únicamente el catálogo a FESE_DEV. No crea bases ni modifica el servidor. La autenticación continúa en la conexión central existente.

Para producción, agregar la clave `ConnectionStrings:FESE` al archivo privado existente `/etc/ingesta-api/appsettings.Production.json` (o `ConnectionStrings__FESE` si el servicio usa variables). Usar la misma conexión SQL vigente cambiando únicamente la base a FESE. Mantener propietario/permisos del archivo. No copiar esa configuración al repositorio. El SQL login existente requiere permisos en las bases nuevas; no se recrea ni se cambia su contraseña.

Antes de publicar: ejecutar scripts SQL y comprobar en desarrollo el recorrido completo. El servicio sigue siendo `ingesta-api`, con su puerto actual 5002. Las rutas nuevas comienzan en `/api/fese`; las rutas DDCP permanecen como estaban.

En Apache, `/fese/api/` debe dirigirse a `http://127.0.0.1:5002/api/`, y `/fese/` al front compilado con `--base-href /fese/`. Agregar el origen HTTPS del sitio a CORS si la configuración productiva lo requiere. No cambiar el proxy DDCP.

No se incluyen credenciales ni se han modificado servicios o bases en esta entrega.

## Corte y actualizaciones

- `GET /api/fese/catalogos` devuelve `corte: { anio, mes }`, calculado con la fecha de Ciudad de México. Corresponde al mes anterior, incluso al cambiar de año.
- `POST /api/fese/cargas/validar` recibe `actualizacion` como campo del formulario (por defecto `false`). La carga inicial admite únicamente el corte y no puede sustituir una entrega aceptada.
- Una actualización requiere `actualizacion=true` y una entrega aceptada de esa entidad y periodo. La comprobación se repite dentro de la transacción de guardado; se conserva la sustitución con histórico y la protección contra cargas base desactualizadas.
- Una carga inicial pendiente de un corte que ya cerró no se puede aceptar fuera de su corte.
- Los nuevos comprobantes siguen el formato institucional mensual: comparativo por categoría, totales, variaciones de al menos 10% en azul y observaciones. La vista previa se identifica como pendiente y no acredita recepción. Los PDF se generan al descargar usando el comparativo guardado y las filas de la versión solicitada. Ejecute el `03_confirmacion.sql` actualizado antes de iniciar la API.

Actualizar API y front juntos. El ajuste del corte y comprobante no requiere SQL adicional; la gestión de permisos sí requiere la migración indicada abajo.

## Permisos de carga y actualizaciones

Antes de iniciar esta versión, ejecutar en FESE_DEV `FESE.BD/estructura/05_permisos_operacion.sql` y volver a ejecutar `03_confirmacion.sql`. Repetir en FESE cuando se publique.

Gestión de usuarios permite abrir/cerrar ambas operaciones de forma general y editar permisos por cuenta. Se administran solo por superusuarios nacionales, quedan en bitácora FESE y se comprueban al validar y al aceptar. Consultas/descargas/cancelaciones continúan disponibles durante el cierre. Los superusuarios también respetan el cierre operativo y conservan la administración para reabrirlo. El perfil CONSULTA nunca obtiene escritura por activar interruptores.

## Enlaces por entidad mediante queries

Los queries listos están en FESE.BD:

- En DDCP_DEV, ejecutar `estructura/06_enlaces_desarrollo.sql`. Cada usuario lleva literalmente el nombre de su entidad del catálogo central; su contraseña es `password01`. Repetir el query restablece esa contraseña y reactiva los enlaces de desarrollo correspondientes. Las cuentas anteriores `fese.enlace.XX` se desactivan únicamente si eran enlaces estatales con acceso exclusivo a FESE. No se borran entregas ni usuarios.
- Ejecutar `estructura/07_cambio_password.sql` en FESE_DEV y FESE. Crea la marca de cambio obligatorio, sin modificar estados existentes.
- En producción, después de la migración en FESE, ejecutar `estructura/08_enlaces_produccion.sql` en DDCP. Las cuentas nuevas reciben la contraseña inicial `password01` y quedan obligadas a cambiarla. Las cuentas existentes y sus contraseñas no se restablecen.

En Development no se pide cambio obligatorio, aunque una cuenta tenga la marca. En producción, una cuenta marcada únicamente puede obtener los catálogos necesarios para acceder al formulario y cambiar su contraseña. La API bloquea los demás endpoints FESE hasta completar el cambio. El formulario comprueba la contraseña actual y exige una nueva distinta que cumpla las reglas vigentes.

La contraseña se actualiza en la autenticación central y se libera la marca FESE dentro de una sola transacción entre las bases del mismo servidor. No se guardan contraseñas de usuario en el front ni en la tabla de marcas. Un cambio central también aplica a otros sistemas a los que esa cuenta ya tenga acceso.

El comando anterior `--crear-enlaces-fese-dev` y su script PowerShell se retiraron. No se han ejecutado estos queries en las bases del usuario desde esta sesión.
