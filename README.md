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

Conservar esa clave y el contenido del comando al reintentar. La clave se reserva por usuario entre los cuatro tipos de movimiento y permanece mientras exista el movimiento. Una repetición confirmada devuelve `200` con el movimiento original, sin cambiar stock ni añadir auditorías. Reutilizar la clave con otro contenido o tipo devuelve `409`. Los movimientos históricos anteriores a esta migración no tienen clave.

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

El total de movimientos y stock solo cuenta filas autorizadas. Leer un movimiento completo de transferencia exige acceso a ambas bodegas; si falta uno de los alcances, se omite del listado y su detalle devuelve `403`. Consultar un filtro explícito de bodega fuera de alcance devuelve `403`. Un UUID de movimiento inexistente devuelve `404`. El kardex de una bodega autorizada puede mostrar su parte de una transferencia sin revelar el detalle de la otra bodega.

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
