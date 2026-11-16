# Inventory ERP

Sistema de inventario para múltiples bodegas. Backend modular con destino .NET 10, SDK .NET 11 y PostgreSQL.

## Documentación

- [Producto](docs/producto.md): requisitos y reglas de negocio.
- [Arquitectura](docs/arquitectura.md): estructura y decisiones técnicas.
- [Estado y backlog](docs/backlog.md): alcance, pendientes y evidencia de validación.

## Ejecución local con PostgreSQL

Requisitos: SDK indicado en `global.json`, runtime ASP.NET Core 10 y PostgreSQL 16 o superior. Para los scripts se necesitan Bash y `psql`. El usuario PostgreSQL debe poder crear bases (`CREATEDB`); las pruebas crean y eliminan únicamente una base temporal con nombre `inventario_erp_test_<uuid>`.

```bash
cp .env.example .env # Si ya existe, conservarlo y agregar las variables nuevas.
# Completar PostgreSQL y Jwt__SigningKey. Generar la clave una sola vez: openssl rand -base64 48
bash scripts/init-db.sh
source .env
dotnet run --project src/Inventory.API --launch-profile http
```

La clave JWT privada es obligatoria y debe configurarse en `Jwt__SigningKey`; los valores vacíos o `CHANGE_ME` impiden el arranque. No existe una clave compartida de respaldo en appsettings.

La configuración local usa `localhost:5432`, el usuario `ozkar` y la base `inventario_erp`. Las credenciales se guardan en `.env`, excluido de Git. En este entorno ya está configurado con las credenciales proporcionadas. `init-db.sh` crea la base si no existe y aplica migraciones y datos iniciales sin levantar el servidor HTTP. Es idempotente y no elimina datos existentes. En Development y Testing el arranque también migra y siembra; una falla detiene el inicio y devuelve un código de error. En Staging y Production la inicialización automática está desactivada por defecto.

La API se publica en `http://localhost:5092` y Swagger en `/swagger`. Administrador inicial de desarrollo: `admin@inventory.local` / `Admin#12345`, con acceso a todas las bodegas.

La cadena `ConnectionStrings__Postgres` configura tanto la API como `dotnet ef`. La factoría de diseño también admite `POSTGRES_CONNECTION` por compatibilidad. Para migrar manualmente con la herramienta EF instalada:

```bash
source .env
dotnet ef database update --project src/Inventory.Persistence --startup-project src/Inventory.Persistence
```

Como alternativa a PostgreSQL instalado localmente, `docker compose up -d postgres` provisiona PostgreSQL 16 en el puerto **5433** con las variables `PGUSER`, `PGPASSWORD` y `PGDATABASE` de `.env`. Cambiar `PGPORT` y los puertos de ambas cadenas en `.env` a 5433 antes de ejecutar los scripts. Las variables de inicialización del contenedor solo se aplican a volúmenes nuevos. Redis y RabbitMQ están disponibles en Compose para etapas posteriores.

## Validación reproducible

```bash
bash scripts/validate.sh
```

El script restaura dependencias, compila Release y ejecuta todas las pruebas. `TEST_POSTGRES_CONNECTION` debe señalar el servidor PostgreSQL y un rol con `CREATEDB`; se carga desde `.env` si existe. La suite **falla** si PostgreSQL o esa variable no están disponibles.

Cada ejecución crea una base vacía, aplica las migraciones reales y verifica el catálogo de permisos, roles y administrador, incluida la idempotencia del seeder. Ejecuta la API con `WebApplicationFactory`, JWT, autorización e interceptor reales; cubre login, catálogos, producto, dos bodegas, entrada de 100, salida de 30 y transferencia de 20. Verifica existencias finales de 50 y 20, movimientos persistidos y ambas caras del kardex desde solicitudes y contextos independientes. Al terminar elimina la base temporal; `inventario_erp` no recibe datos de prueba. Si el proceso se interrumpe abruptamente puede quedar una base temporal para limpieza manual.

Los resultados TRX quedan en `TestResults/`. GitHub Actions ejecuta el mismo script en cada push y pull request, con PostgreSQL 16 como servicio efímero y credenciales exclusivas de CI; publica los TRX como artefacto.

Para ejecutar únicamente las pruebas existentes sin PostgreSQL:

```bash
dotnet test tests/Inventory.Domain.Tests
dotnet test tests/Inventory.Authorization.Tests
dotnet test tests/Inventory.Application.Tests
```

## Contrato de movimientos e integridad

Los cuatro POST de `/api/v1/inventory` requieren `idempotencyKey`, un UUID no vacío generado por el cliente para cada operación. Ejemplo de entrada:

```json
{
  "idempotencyKey": "abf9c832-554d-4461-8515-a40a2048ecbb",
  "warehouseId": "9c90e0c8-bcaf-4e3a-b191-3a179560e3e9",
  "lines": [{ "productId": "f85dcbdf-b38e-4ca1-bacd-a28041db5044", "quantity": 10, "unitCost": 2 }]
}
```

Una creación confirmada devuelve `201 Created` con `Location` hacia `/api/v1/inventory/movements/{id}`. Conservar esa clave y el contenido del comando al reintentar. La clave se reserva por usuario entre los cuatro tipos de movimiento y permanece mientras exista el movimiento. Una repetición confirmada devuelve `200` con el movimiento original, sin cambiar stock ni añadir auditorías. Reutilizar la clave con otro contenido o tipo devuelve `409`. Los movimientos históricos anteriores a esta migración no tienen clave.

Un conflicto de escritura simultánea devuelve `409 ProblemDetails` y revierte toda la operación. El cliente puede reintentar la solicitud completa con la misma clave y contenido, usando una espera entre intentos. Esto también aplica a duplicados que llegan simultáneamente: uno confirma y el otro puede recibir `409`; su siguiente intento devuelve el resultado confirmado. Un reintento se valida contra el saldo actual y puede recibir `422`, por ejemplo si ya no hay stock suficiente. Las solicitudes fallidas no consumen su clave.

Cada producto puede aparecer una sola vez en un movimiento; los duplicados y las claves ausentes devuelven `400`. Cantidades y costos deben caber en `numeric(18,4)` (hasta 14 dígitos enteros y 4 decimales); se rechaza el exceso de precisión para evitar redondeos que desajusten el kardex. Los ceros decimales finales no cambian la huella de idempotencia: `10` y `10.0` son equivalentes. Los ajustes conservan el contrato de cantidad absoluta positiva y justificación obligatoria. Una transferencia exige acceso a ambas bodegas. Todas las solicitudes, incluidos los reintentos, comprueban los permisos y alcances actuales. Las consultas sin bodega devuelven únicamente datos autorizados; consultar explícitamente una bodega fuera de alcance devuelve `403`.

La migración `InventoryIntegrity` incorpora versiones de stock, la unicidad de claves por usuario y una secuencia para reconstruir el kardex independientemente del reloj de las solicitudes. Conserva el orden histórico anterior y limpia campos de contraseñas, hashes y tokens previamente guardados en auditoría. Esta limpieza de datos sensibles es irreversible; la auditoría nueva usa una lista explícita de propiedades permitidas.

La suite PostgreSQL incluye aislamiento por bodega, Allow/Deny, limpieza de auditoría, actualización de una base existente, movimientos simultáneos, reintentos y errores en ajustes y transferencias. Las pruebas coordinan dos solicitudes antes de guardar para comprobar conflictos reales, tanto con stock existente como al crearlo. El límite de solicitudes se desactiva solo en el servidor de pruebas para permitir ejecutar la suite; autenticación, autorización, PostgreSQL e interceptor siguen activos.

## Administración de usuarios y sesiones (punto 3)

- `PUT /api/v1/users/{id}`: reemplaza `email`, `userName`, `fullName` e `isActive`. Requiere `users.manage`. Desactivar revoca los refresh tokens existentes.
- `GET /api/v1/users/{id}/access`: devuelve roles, excepciones y alcances; requiere `users.manage`.
- `PUT /api/v1/users/{id}/access`: reemplaza el conjunto completo de accesos. Requiere **ambos** permisos `users.manage` y `roles.manage`, al igual que crear usuarios. Las listas vacías retiran asignaciones; una excepción con el mismo permiso actualiza su tipo.
- `GET /api/v1/permissions?offset=0&limit=100`: catálogo de UUID y códigos, requiere `roles.manage`; máximo 100 resultados.
- `GET /api/v1/users/me`: perfil, permisos efectivos y UUID de bodegas del usuario autenticado, sin exigir permisos administrativos.
- `POST /api/v1/auth/logout`: revoca todos los refresh tokens del usuario autenticado, en todos los dispositivos; responde `204` y admite repetición.
- `DELETE /api/v1/users/{id}/sessions`: revocación administrativa de todos los refresh tokens del usuario; requiere `users.manage`.

Ejemplo del cuerpo de actualización de accesos:

```json
{
  "roleIds": ["11111111-1111-1111-1111-111111111111"],
  "overrides": [{"permissionId": "22222222-2222-2222-2222-222222222222", "type": 2}],
  "hasAllWarehousesAccess": false,
  "warehouseIds": ["33333333-3333-3333-3333-333333333333"]
}
```

`type` es `1` (Allow) o `2` (Deny). Deny prevalece sobre roles y Allow mediante el motor existente. El acceso global exige `warehouseIds: []`; retirarlo deja únicamente las bodegas enviadas. UUID inexistentes producen `404`, entradas inválidas `400` y correos duplicados `409`, sin guardar modificaciones parciales. La creación también comprueba todos los UUID de bodegas recibidos.

**Delegación de accesos (OZK-46):** `POST /api/v1/users` y `PUT /api/v1/users/{id}/access` conservan sus permisos administrativos y contratos de solicitud existentes, y aplican además las mismas reglas de delegación. No se puede modificar el propio acceso, incluso con permisos de administrador. Los permisos concedidos por los roles del destinatario y sus excepciones Allow deben ser un subconjunto de los permisos efectivos del solicitante; un Deny del solicitante reduce ese límite, y un Deny en el destinatario no sirve para ocultar permisos excesivos concedidos por roles. Solo quien ya tiene acceso global puede delegarlo. Quien tiene alcance limitado solo puede asignar bodegas dentro de su propio alcance. Las infracciones devuelven `403` antes de guardar cambios.

**Protección de administradores (OZK-47, 2026-11-14):** un usuario no puede desactivarse ni cambiar su propio email; ambas operaciones devuelven `403`. El email se normaliza con trim y minúsculas, por lo que una variante equivalente se permite. Desactivar al último usuario activo con el rol Administrator o retirarle ese rol devuelve `409`; un administrador inactivo no cuenta como reemplazo. Las actualizaciones de perfil y accesos toman el mismo bloqueo advisory dentro de una transacción `READ COMMITTED`, para que las operaciones concurrentes comprueben el conjunto ya confirmado. Al editar un administrador se comprueba que sus roles y permisos Allow actuales y resultantes estén contenidos en los permisos efectivos del actor; Deny del actor prevalece y se valida también el acceso global o las bodegas frente al alcance del actor. Los cambios, revocación de refresh tokens y auditoría se confirman juntos; los eventos se despachan tras el commit. No hubo migraciones ni dependencias nuevas. Los controles generales de creación, delegación y autoedición de accesos de OZK-46 se aplican conjuntamente.

Validación integrada de OZK-46 y OZK-47 en el checkout principal, el 2026-11-14: **161/161 pruebas aprobadas**, 0 fallidas y 0 omitidas (API/PostgreSQL 119, Application 7, Authorization 7, Domain 28); compilación Release con 0 advertencias y 0 errores, formato .NET y `git diff --check` correctos. Los TRX están en `TestResults/` con marcas de tiempo `2026-11-14_21_04_26` y `2026-11-14_21_04_27`. La validación previa de OZK-47 con 151 pruebas fue independiente y sus TRX pertenecen a su worktree.

**Política de sesiones:** logout y revocación administrativa invalidan la renovación, pero los JWT emitidos continúan hasta su expiración (15 minutos por defecto, más la tolerancia de reloj de 30 segundos). Los permisos y alcances se consultan en PostgreSQL en cada solicitud; no dependen de los roles antiguos del JWT. Cada JWT comprueba además que el usuario siga activo: al desactivarlo devuelve `401`, y login/refresh denegados conservan el `403` del contrato existente. Reactivar exige un nuevo login para obtener refresh tokens; un JWT previo aún vigente vuelve a ser aceptado al reactivar. No se añade una blacklist ni nuevas dependencias o migraciones.

## Consultas operativas

| Endpoint | Permiso | Filtros |
|---|---|---|
| `GET /api/v1/audit-logs` | `audit.read` | `userId` (actor), `entity`, `action`, `fromUtc`, `toUtc` |
| `GET /api/v1/inventory/movements` | `inventory.read` | `warehouseId`, `productId`, `type`, `fromUtc`, `toUtc` |
| `GET /api/v1/inventory/movements/{id}` | `inventory.read` | Detalle por UUID |
| `GET /api/v2/inventory/stock` | `inventory.read` | `warehouseId`, `productId` |
| `GET /api/v2/inventory/kardex` | `inventory.read` | `productId` y `warehouseId` obligatorios, `fromUtc`, `toUtc` |

Los listados de esta tabla usan el contrato existente `PagedResult`: `{ items, page, pageSize, totalCount, totalPages }`. `page` empieza en 1; `pageSize` vale 20 por defecto y admite de 1 a 200. Los parámetros inválidos devuelven `400 ProblemDetails`. Una página posterior a la última devuelve `items: []` conservando el total filtrado. Productos también mantiene su paginación, ahora con desempate estable por UUID.

Los filtros de fechas son inclusivos y reciben ISO 8601 UTC, por ejemplo `2026-11-12T00:00:00Z`. Los tipos de movimiento son `Entry`, `Exit`, `Transfer` y `Adjustment`; las acciones de auditoría son `Created`, `Updated` y `Deleted`. Los filtros se combinan. Auditoría ordena por fecha descendente e ID descendente; movimientos por secuencia descendente; stock por bodega y producto; kardex por secuencia ascendente. La paginación es por desplazamiento y refleja el estado al consultar, no una instantánea persistida entre solicitudes.

Las consultas de stock y kardex de `/api/v1` conservan su respuesta histórica en forma de arreglo para compatibilidad. Los clientes nuevos deben usar `/api/v2` para disponer de paginación. Swagger de Development separa ambos contratos en `/swagger/v1/swagger.json` y `/swagger/v2/swagger.json`.

El total de movimientos y stock solo cuenta filas autorizadas. Leer un movimiento completo de transferencia exige acceso a ambas bodegas; si falta uno de los alcances, se omite del listado y su detalle devuelve `404`, igual que un movimiento inexistente (`Movement was not found.`). El detalle filtra por alcance antes de buscar el UUID. Consultar un filtro explícito de bodega fuera de alcance devuelve `403`, con un mensaje genérico sin UUID de bodega; la misma regla de mensaje se aplica a las escrituras fuera de alcance. El kardex de una bodega autorizada puede mostrar su parte de una transferencia sin revelar el detalle de la otra bodega.

Los saldos del kardex se calculan con el historial anterior completo, incluyendo ajustes, antes de presentar la página y el rango de fechas. No se reinicia el saldo al cambiar de página. La consulta paginada se ejecuta en PostgreSQL y no carga todo el historial en memoria.

`audit.read` es un permiso global de investigación: permite leer la auditoría del sistema, independientemente del alcance de bodegas. Debe otorgarse solo a quienes deban investigar cambios de acceso y operaciones. Se registran roles, permisos, sus asignaciones, excepciones Allow/Deny y alcances, además de las entidades ya auditadas. Los JSON nuevos incluyen el ID de la entidad/asignación y sus referencias; los registros históricos conservan el contenido disponible al crearse. Las bajas de asignaciones conservan los valores anteriores. Auditoría y cambio de negocio se guardan en la misma transacción; una reversión descarta ambos. Las operaciones de administración del punto 3 también quedan cubiertas por este interceptor.

Los errores de las consultas, los rechazos de autorización y readiness usan `application/problem+json`. El `correlationId` permite relacionarlos con la auditoría y los logs estructurados; las respuestas de error no incluyen SQL, credenciales ni stack traces.

## Ambientes y disponibilidad

| Ambiente | Inicialización al arrancar | Administrador inicial | Swagger |
|---|---|---|---|
| Development | Sí | Administrador local documentado, salvo configuración explícita | Sí |
| Testing | Sí | Administrador de prueba; la suite genera su propia clave JWT | No |
| Staging / Production | No | Solo si se proporcionan credenciales privadas explícitas al inicializar | No |

`appsettings.json` conserva configuración común sin contraseñas ni clave JWT; los archivos de ambiente definen los comportamientos de inicialización. Las variables de entorno tienen precedencia. Se validan al arrancar conexión PostgreSQL, emisor y audiencia JWT, clave de firma de al menos 32 bytes, duración del acceso (1–60 minutos), refresh (1–30 días) y la pareja opcional de credenciales iniciales. `Database__InitializeOnStartup` permite configurar explícitamente la inicialización; `--migrate-only` la ejecuta de forma deliberada y termina sin abrir HTTP.

Para Staging/Production, proporcionar `ConnectionStrings__Postgres`, `Jwt__SigningKey`, `ASPNETCORE_ENVIRONMENT` y, si difieren de los valores comunes, `Jwt__Issuer`/`Jwt__Audience`. Crear una base independiente y aplicar las migraciones con un rol autorizado antes de servir tráfico:

```bash
# Inyectar secretos mediante el entorno o el gestor de secretos del despliegue.
# Para crear el primer administrador, definir también BootstrapAdmin__Email y
# BootstrapAdmin__Password (privada, de al menos 12 caracteres).
dotnet run --project src/Inventory.API --no-launch-profile -- --migrate-only
# Retirar la contraseña inicial del entorno una vez provisionada la cuenta.
dotnet run --project src/Inventory.API --no-launch-profile
```

Sin esa pareja de credenciales, Production/Staging no crean un administrador de contraseña conocida. La inicialización no cambia contraseñas ni roles de usuarios existentes. Mantener la misma clave JWT privada entre reinicios y entre instancias. `.env` solo sirve para desarrollo y sigue excluido de Git; `.env.example` documenta las variables nuevas. Redis y RabbitMQ no son dependencias activas en esta entrega.

- `/health`: liveness pública; devuelve `200 Healthy` sin consultar PostgreSQL.
- `/ready`: readiness pública; comprueba conexión y migraciones pendientes con un presupuesto de 3 segundos. Devuelve `200` cuando la base está preparada y `503 ProblemDetails` cuando no lo está, sin publicar detalles internos de conexión. También funciona al iniciar con inicialización automática desactivada y PostgreSQL caído.

La suite incluye filtros y permisos de auditoría, altas/cambios/bajas de asignaciones y acceso global, rollback de auditoría, movimientos entre distintas bodegas, paginación con fechas y ajustes, orden con fechas iguales o reloj regresivo, configuración inválida, separación Swagger y readiness ante caída de PostgreSQL o migraciones pendientes. Todos los casos de persistencia usan bases temporales.

### Límites de autenticación y proxies (OZK-51)

`POST /api/v1/auth/login` y `POST /api/v1/auth/refresh` comparten la política `authentication`: por defecto, 10 solicitudes por IP y 5 por cuenta en una ventana de 60 segundos, sin cola. Cuentan los intentos exitosos y fallidos; agotar el presupuesto bloquea temporalmente esa IP o cuenta hasta que termina su ventana. El email se normaliza igual que en login (trim y minúsculas); refresh resuelve la cuenta desde el hash del token, incluso si ya está revocado. Los tokens desconocidos quedan protegidos por el límite de IP. Las particiones de cuenta retienen una huella SHA-256 del email, nunca contraseñas ni tokens. IPv4 y su representación IPv6 mapeada comparten presupuesto; las IPv6 se agrupan por `/64`.

Se configuran con `AuthRateLimit__IpPermitLimit`, `AuthRateLimit__AccountPermitLimit` y `AuthRateLimit__WindowSeconds`. Se rechazan al arrancar cuotas fuera de 1–100000 o ventanas fuera de 1–3600 segundos. Un rechazo devuelve `429 application/problem+json`, `correlationId` y `Retry-After` en segundos. La API conserva además su límite global de 100 solicitudes/minuto por IP. Los contadores están en memoria por instancia y se reinician al reiniciar la API; con varias réplicas, aplicar también un límite compartido en el gateway del despliegue.

Los encabezados `X-Forwarded-For` y `X-Forwarded-Proto` se procesan antes del limitador y de los logs. Solo se aceptan desde loopback y los proxies o redes expresamente configurados en `ReverseProxy__KnownProxies__0`, `ReverseProxy__KnownProxies__1`, etc., o `ReverseProxy__KnownNetworks__0` (CIDR). Configurar únicamente los proxies reales, que deben sobrescribir los encabezados recibidos de clientes. No se permiten redes `/0`. `ReverseProxy__ForwardLimit` vale 1 por defecto y admite 1–10 saltos; para una cadena de proxies, declarar cada salto de confianza y su límite. Un cliente directo no puede cambiar de presupuesto falsificando esos encabezados.

Las pruebas de integración del limitador mantienen activa la política y comprueban presupuestos por IP y cuenta, login/refresh compartidos, recuperación tras la ventana, IPv6 `/64`, proxies conocidos y encabezados falsificados desde pares desconocidos. El resto de las suites usa cuotas altas explícitas para poder ejecutar sus regresiones sin agotar la cuota de la IP o cuenta compartida de pruebas.

### Respuestas de autenticación (OZK-50 y OZK-55)

Login ejecuta una verificación BCrypt incluso cuando no existe la cuenta: usa un hash ficticio precalculado con el mismo factor 12 que los hashes reales. El estado activo del usuario se comprueba después de verificar la contraseña; una cuenta inexistente, inactiva o con contraseña incorrecta recibe el mismo mensaje `Invalid credentials.`. El hash ficticio no permite iniciar sesión, aunque se conozca su contraseña.

`POST /api/v1/auth/login` con credenciales inválidas y `POST /api/v1/auth/refresh` con un token desconocido, vencido, revocado o perteneciente a una cuenta inactiva responden `401 application/problem+json`, con `WWW-Authenticate: Bearer` y el ID de correlación. Los cuerpos incompletos o inválidos siguen siendo errores de validación `400`; la falta de permisos de un usuario autenticado sigue produciendo `403`. Los límites de autenticación mantienen su respuesta `429` y `Retry-After`. La detección de reutilización de un refresh ya reemplazado conserva la revocación de sesiones y auditoría de OZK-49 antes de responder `401`.

### Autorización por solicitud y consultas de usuarios (OZK-52, OZK-53, OZK-54)

`IUserAuthorizationProvider` se comparte mediante un decorador scoped: memoriza por UUID de usuario, comparte las cargas concurrentes y permite reintentar si una carga falla o se cancela. El proveedor consulta mediante cuatro proyecciones independientes (usuario, permisos de roles, excepciones y bodegas), sin unir las tres colecciones entre sí. `PermissionAuthorizationHandler` y `PermissionChecker` usan la misma caché. Una solicitud nueva carga datos nuevos. Las actualizaciones administrativas invalidan el snapshot del actor después de tomar el bloqueo transaccional, antes de comprobar nuevamente sus permisos, para impedir escrituras con permisos revocados mientras esperaban.

Medición reproducible con PostgreSQL en `AuthorizationCacheTests.Repeated_authorization_reads_execute_one_bounded_load`: tres lecturas del mismo usuario ejecutaban **9 SELECT** antes del cambio y ejecutan **4 SELECT** después. La medida cuenta las consultas del proveedor; no incluye la consulta JWT que comprueba que el usuario siga activo. Las pruebas HTTP verifican también una única carga compartida entre las dos políticas y el handler de creación, y entre la política de inventario y sus comprobaciones de bodegas. No se afirma una reducción de latencia no medida.

| Endpoint | Contrato y límites |
| --- | --- |
| `GET /api/v2/users?page=1&pageSize=20` | `PagedResult<UserDto>`; orden email + UUID; nombres de roles solo para los usuarios de la página; permiso `users.manage`. |
| `GET /api/v2/permissions?page=1&pageSize=20` | `PagedResult<PermissionCatalogItem>`; orden código + UUID; permiso `roles.manage`. |
| `GET /api/v1/users?page=1&pageSize=20` | Conserva el array de usuarios; ahora carga únicamente la página solicitada. |
| `GET /api/v1/permissions?offset=0&limit=100` | Conserva su array y los parámetros anteriores, con límite máximo 100. |

Los contratos v2 devuelven `items`, `page`, `pageSize`, `totalCount` y `totalPages`. `pageSize` admite 1–200 y `page` 1–10737418; entradas fuera de rango devuelven `400`, sin desbordar el offset. Las páginas posteriores al último resultado devuelven `items: []` con el total correspondiente. Los clientes que necesitan recorrer todo el catálogo deben consumir páginas, preferentemente de v2. Los permisos y la exigencia de autenticación son los mismos que en v1; los DTO no contienen hashes ni tokens.

OZK-54 completa las pruebas unitarias de `CreateUserCommandValidator` y `UpdateUserAccessValidator`, incluidos límites de nombres/contraseñas, colecciones nulas, elementos nulos, tipos de excepción inválidos, permisos duplicados, UUID vacíos y contradicciones entre alcance global e individual. Las regresiones de login inexistente/incorrecto/inactivo ya están en `LoginSecurityTests` y `AuthenticationFailureTests`; la rotación concurrente y la reutilización están en `UserAdministrationTests`, y los límites activos en `AuthRateLimitTests`. Se conservan y ejecutan esas pruebas como parte de la suite completa.

### Validación compartida y escritura de movimientos (OZK-56, OZK-44, OZK-64)

Crear y actualizar usuarios comparten las reglas de perfil: email y nombre completo de hasta 256 caracteres, usuario de hasta 128, email válido y nombre de usuario no vacío. El nombre completo puede estar vacío, pero no ser nulo. Crear usuarios y actualizar accesos comparten la validación de alcance: la colección de bodegas no puede ser nula y el acceso global exige que esté vacía. Las solicitudes inválidas devuelven `400` antes de escribir datos.

Crear usuarios y login rechazan contraseñas de más de 128 caracteres o más de 72 bytes UTF-8, para evitar truncamiento en BCrypt. El alta conserva su mínimo de 8 caracteres; login permite intentar contraseñas cortas ya existentes. Por ejemplo, 72 caracteres ASCII o 36 letras `é` ocupan exactamente 72 bytes; 37 letras `é` exceden el límite. Las credenciales explícitas del administrador inicial también respetan el límite de bytes al validar configuración, conservando su mínimo de 12 caracteres.

Los cuatro handlers de movimientos delegan en `InventoryMovementWriter`: alcance, repetición idempotente, entidades activas, mapeo, numeración, aplicación de stock, huella y persistencia. Cada tipo aplica su propia estrategia de stock. Las líneas comparten `MovementLinesValidator`: productos no repetidos, UUID no vacío, cantidad positiva, costo no negativo y precisión `(18,4)`, admitiendo ceros decimales finales. Los ajustes conservan la justificación y el mensaje de cantidad positiva. No se cambia el contrato JSON ni la huella idempotente de los comandos.

Todos los movimientos nuevos sobre productos o bodegas inactivos responden `422`, sin guardar movimientos, stock ni auditoría de movimientos. Las transferencias comprueban ambas bodegas. Las referencias inexistentes siguen devolviendo `404`; las repeticiones idempotentes válidas conservan su resultado sin escribir nuevamente. La decisión sobre salidas desde bodegas cerradas está documentada en [producto.md](docs/producto.md).

### Lotes y consultas de inventario (OZK-65, OZK-66, OZK-67)

Cada movimiento admite como máximo **500 líneas**, mediante `MovementInputLimits.MaxLinesPerMovement`. La validación rechaza más líneas con `400` y omite las validaciones detalladas del lote excesivo. Notas y justificaciones admiten hasta **1024 caracteres**. La escritura carga todos los stocks necesarios en un solo SELECT, incluidas ambas bodegas de una transferencia, y crea en memoria los faltantes. Conserva la concurrencia optimista y la persistencia atómica.

El stock no puede superar `99999999999999.9999`, el máximo de `numeric(18,4)`. Un desbordamiento acumulado devuelve `422` sin guardar cambios parciales; una colisión de número de movimiento devuelve `409`, conservando stock, movimientos y auditoría.

El kardex usa funciones de ventana en PostgreSQL para conservar ajustes absolutos, ambas caras de las transferencias, secuencia estable y saldos de apertura antes de aplicar fechas o paginación. La implementación y comparación reproducible de `EXPLAIN ANALYZE` están en [performance/kardex.md](docs/performance/kardex.md).

Los GET v1 de `/api/v1/inventory/stock` y `/api/v1/inventory/kardex` conservan sus arrays, pero devuelven como máximo **200 filas**. Kardex v1 valida UUID y fechas con las mismas reglas que v2. Ambos anuncian deprecación desde el **16 de noviembre de 2026** y retiro previsto el **16 de mayo de 2027**, mediante `Deprecation`, `Sunset` y un enlace `successor-version`. El frontend debe usar **v2 desde su primera implementación** para obtener `PagedResult`, totales y todas las páginas. Las fechas de `Deprecation` y `Sunset` usan sus respectivos formatos definidos en [RFC 9745](https://www.rfc-editor.org/rfc/rfc9745.html) y [RFC 8594](https://www.rfc-editor.org/rfc/rfc8594.html).

Las pruebas nuevas cubren 500/501 líneas, una consulta de stock por lote, notas y justificaciones de 1024/1025 caracteres, desbordamiento acumulado, colisión de numeración, validación de fechas v1 y límites/cabeceras de deprecación. Se mantienen las regresiones de entidades inactivas, idempotencia, concurrencia, atomicidad y ajustes del kardex.


### Contrato de escritura de movimientos (OZK-73 y OZK-74)

Los cuatro POST de entradas, salidas, transferencias y ajustes devuelven el mismo `MovementDto` como cuerpo. La creación nueva responde `201` y `Location` apunta al detalle existente del movimiento. La repetición idempotente confirmada responde `200`, sin `Location`, con el DTO original y sin cambios en stock o auditoría. El indicador `IsReplay` solo forma parte del resultado interno de aplicación: no se añade al JSON. OpenAPI documenta ambos estados de éxito.

| Estado | Resultado |
| --- | --- |
| `201` | Movimiento nuevo confirmado; incluye `Location`. |
| `200` | Repetición idempotente válida del movimiento existente. |
| `400` | Entrada inválida, límites o clave ausente. |
| `401` | Usuario no autenticado o identificador de usuario ausente/vacío. |
| `403` | Falta de permiso o alcance sobre una bodega. |
| `404` | Referencias de producto o bodega inexistentes. |
| `409` | Conflicto de idempotencia, concurrencia o numeración. |
| `422` | Regla de negocio: stock disponible insuficiente, entidades inactivas o límites acumulados. |

Las salidas y transferencias tratan la ausencia de una fila de stock como cantidad cero. Stock ausente, cero, insuficiente o reservado responden `422` con `Insufficient available stock.`, sin UUID internos en el mensaje y sin persistir filas cero ni cambios parciales. La guarda de autenticación se ejecuta al inicio del writer, antes de alcance, repetición o escritura, y produce `401` con el desafío Bearer.

El enlace de `Location` conserva el permiso `inventory.read` y el alcance del detalle, incluidas ambas bodegas en transferencias. Crear un movimiento con permiso de escritura no concede permiso de lectura adicional. Este ajuste de estados se incorpora antes de implementar los formularios del frontend.

### Integridad relacional del inventario (OZK-71)

La migración `20261116052329_InventoryRelationalConstraints` añade claves foráneas de stock y líneas hacia productos, y de stock y movimientos hacia bodegas (incluido el destino opcional). Todas usan `RESTRICT`, al igual que la relación entre movimiento y líneas: borrar físicamente un movimiento con líneas ya no elimina su historial en cascada. Esto no impide modificar o borrar líneas directamente mediante SQL; no se añaden triggers de inmutabilidad. Las bajas operativas de productos y bodegas siguen siendo desactivaciones.

PostgreSQL exige stock no negativo, reservas entre cero y la cantidad física, líneas con cantidad positiva y costo no negativo. Se añaden índices de bodega en stock y de bodega destino en movimientos. Las cantidades cero de stock, las reservas iguales al stock y los movimientos sin destino siguen siendo válidos.

Antes de desplegar, ejecutar `scripts/check-inventory-integrity.sql` con `psql` conectado a la base que se va a actualizar, por ejemplo `psql --dbname=inventario_erp --file=scripts/check-inventory-integrity.sql` con la conexión configurada en el entorno de PostgreSQL. Los tres conteos deben ser cero. La migración repite esta comprobación antes de cambiar restricciones y falla con `Inventory integrity precheck failed` si encuentra datos inválidos. No borra, rellena ni corrige datos históricos: investigar las filas afectadas y resolverlas explícitamente antes de reintentar.

La migración conserva columnas, saldos, movimientos, secuencias y claves de idempotencia. Validar restricciones e índices requiere leer las tablas y tomar bloqueos durante la transacción; planificar su ejecución según el volumen de datos y las escrituras concurrentes. El rollback elimina las nuevas restricciones e índices y restaura la cascada anterior entre movimientos y líneas. Las pruebas verifican bases nuevas, actualización desde el esquema inicial con datos válidos, rechazo sin cambios de datos inválidos y reintento tras corregir únicamente los datos de prueba.
