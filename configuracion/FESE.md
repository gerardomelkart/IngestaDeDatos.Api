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
- Los nuevos comprobantes siguen el formato institucional mensual: comparativo por categoría, totales, variaciones de al menos 10% en azul y observaciones. La vista previa se identifica como pendiente y no acredita recepción. Los PDF definitivos ya almacenados conservan su contenido original.

Actualizar API y front juntos. El ajuste del corte y comprobante no requiere SQL adicional; la gestión de permisos sí requiere la migración indicada abajo.

## Permisos de carga y actualizaciones

Antes de iniciar esta versión, ejecutar en FESE_DEV `FESE.BD/estructura/05_permisos_operacion.sql` y volver a ejecutar `03_confirmacion.sql`. Repetir en FESE cuando se publique.

Gestión de usuarios permite abrir/cerrar ambas operaciones de forma general y editar permisos por cuenta. Se administran solo por superusuarios nacionales, quedan en bitácora FESE y se comprueban al validar y al aceptar. Consultas/descargas/cancelaciones continúan disponibles durante el cierre. Los superusuarios también respetan el cierre operativo y conservan la administración para reabrirlo. El perfil CONSULTA nunca obtiene escritura por activar interruptores.

## Enlaces de desarrollo por entidad

Desde la raíz del repositorio de la API:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ".\configuracion\crear-enlaces-fese-dev.ps1"
```

Capturar una contraseña de prueba que cumpla las reglas actuales. Se usa solo para cuentas nuevas. Se crean `fese.enlace.01` a `fese.enlace.32`, con rol ENLACE, alcance estatal y acceso únicamente a FESE. Los nombres y entidades se consultan en las bases, sin usuarios simulados en el front.

El comando se niega a operar fuera de Development, contra autenticación distinta de DDCP_DEV, si faltan entidades o si hay cuentas con el mismo nombre y otra configuración. Es transaccional y repetible: no sobrescribe cuentas ni contraseñas existentes. No crea cuentas en producción. La sesión actual no tiene acceso al SQL del usuario: la creación efectiva se realiza al ejecutar el comando local.

Con el superusuario se ven las 32 entidades y Gestión de usuarios. Con `fese.enlace.03`, por ejemplo, se entra únicamente a la entidad 03 del catálogo; no se muestra administración ni descarga CNIEDT nacional. Para otra entidad, usar su número oficial de dos dígitos.
