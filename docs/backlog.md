# Estado y backlog — Inventory ERP

Fecha de planificación: 2026-11-12.

Objetivo: cerrar y validar la fase 1 del backend, construir una interfaz operativa y avanzar después con compras y reportes.

Este documento reúne el estado histórico, los pendientes y la planificación acordada. Los puntos 1–4 están implementados y validados localmente; los puntos 5 y 6 siguen pendientes. Las casillas y la evidencia indican el estado comprobado de cada tarea. Los requisitos se mantienen en [producto.md](producto.md) y las decisiones técnicas en [arquitectura.md](arquitectura.md).

Consolidación documental: 2026-11-12. El contenido de `docs/project.md` se integró aquí; las instrucciones de ejecución están en el [README](../README.md).

## Estado de partida

El estado histórico del 2026-06-08 reporta la base y gran parte del backend de fase 1 implementadas: 41 pruebas aprobadas (Domain 27, Authorization 7, Application 7), compilación sin advertencias ni errores y pruebas de aplicación con EF en memoria. Es evidencia documental anterior; no se volvieron a ejecutar esas comprobaciones al consolidar estos archivos.

La revisión de código del 2026-11-12 identificó los problemas de autorización, auditoría y concurrencia detallados en el paso 2. Se corrigieron y validaron localmente ese mismo día: **76 pruebas aprobadas** (Domain 28, Authorization 7, Application 7, API/PostgreSQL 34), compilación Release sin advertencias ni errores. La validación incluye acceso por bodega, auditoría sin credenciales, idempotencia, errores y concurrencia sobre PostgreSQL real. El punto 3 agrega administración de accesos y sesiones (véase su evidencia de cierre). El punto 4 agrega consultas operativas, auditoría paginada y readiness (véase su evidencia de cierre). Quedan pendientes las funciones de los puntos 5 y 6; esto no equivale al cierre de toda la fase 1 ni a una validación de producción.

### Funciones documentadas en la fase 1


#### Identity & Auth
- ✅ JWT login + refresh-token rotation (hashed refresh tokens).
- ✅ RBAC + per-user Allow/Deny overrides + warehouse scopes — engine
  `EffectivePermissions = Roles ∪ Allow − Deny` with warehouse-scope validation (ADR-003).
- ✅ `[RequirePermission(...)]` policy provider/handler over business-oriented permission codes.
- ✅ Seeder: full permission catalog, 5 default roles, initial admin user.

#### Catalog
- ✅ Products: create, update, soft-delete (deactivate), get-by-id, paged/searchable list.
- ✅ Categories, Brands, Units of Measure: create + list.

#### Warehouses
- ✅ Create, update, list (multi-warehouse first — ADR-002).

#### Inventory (core)
- ✅ Movement-based stock (ADR-001): Entry, Exit, Transfer, Adjustment — stock is never edited directly.
- ✅ Per-warehouse `Stock` with `AvailableQuantity = Quantity − ReservedQuantity`; negative stock rejected.
- ✅ Adjustments require justification; transfers are atomic across two warehouses.
- ✅ Kardex (stock ledger) with running balance; stock query.
- ✅ `StockBelowMinimum` domain event raised when stock drops under the product minimum.

#### Audit
- ✅ Immutable `AuditLog` written by a `SaveChanges` interceptor (user, timestamp, entity, action,
  old/new values as `jsonb`, IP, correlation id) for critical entities.

#### Cross-cutting
- ✅ CorrelationId middleware (every request), RFC 7807 ProblemDetails error handling.
- ✅ JWT bearer auth, fixed-window rate limiting, Swagger with bearer button, `/health`.
- ✅ Serilog structured logging, OpenTelemetry tracing, Prometheus metrics at `/metrics`.
- ✅ In-process domain-event dispatch after commit (the seam for the future RabbitMQ publisher).

#### API surface (`/api/v1`)
| Area | Endpoints |
|---|---|
| Auth | `POST /auth/login`, `POST /auth/refresh` |
| Products | `GET/POST /products`, `GET/PUT/DELETE /products/{id}` |
| Catalog | `GET/POST /categories`, `/brands`, `/units-of-measure` |
| Warehouses | `GET/POST /warehouses`, `PUT /warehouses/{id}` |
| Inventory | `POST /inventory/{entries,exits,transfers,adjustments}`, `GET /inventory/{kardex,stock,movements}`, `GET /inventory/movements/{id}` |
| Audit | `GET /audit-logs` (paginado) |
| Users/Roles | `GET/POST /users`, `GET /roles` |


Las consultas paginadas de stock y kardex están en `/api/v2/inventory/{stock,kardex}`; v1 conserva sus arreglos para compatibilidad.

### Verificación histórica


- ✅ `dotnet build InventoryErp.slnx` — clean.
- ✅ `dotnet test InventoryErp.slnx` — 41 tests green, including an in-memory end-to-end inventory
  flow (entry/exit/transfer → stock + kardex running balance).
- ✅ API boots and serves `/health` (`Healthy`), `/swagger`, returns `401` on protected routes, emits
  the `X-Correlation-Id` header, and degrades to `500 ProblemDetails` (no crash) when the DB is down.
- ✅ **Validado el 2026-11-12:** login → producto → entrada → salida → transferencia → kardex contra PostgreSQL real. Véase la evidencia del punto 1.


### Límites actuales

- Usuarios: creación, listado, edición, activación/desactivación y administración de roles, excepciones y alcances disponibles; véase el punto 3.
- Auditoría: escritura con propiedades permitidas, asignaciones y alcances; consulta paginada protegida por `audit.read`, con filtros por actor, entidad, acción y fechas.
- Reservas: `ReservedQuantity` y operaciones Reserve/Release en el dominio, sin casos de uso ni endpoints.
- Sesiones: logout y revocación administrativa de refresh tokens disponibles. Los JWT conservan su vencimiento y verifican usuario activo y permisos actuales; sin blacklist en Redis.
- `/health` comprueba el proceso; `/ready` comprueba PostgreSQL y migraciones, con respuesta `503` cuando la base no está preparada.
- Redis y RabbitMQ están provisionados en Docker, pero no integrados funcionalmente. El despachador de eventos registra mensajes en el proceso.
- `Shared.Events` es un espacio reservado para la fase 3.
- Fases 2–4 y frontend sin iniciar según el estado documentado.

## Fases del producto

| Fase | Alcance previsto |
|---|---|
| 1 | Autenticación, usuarios, roles, permisos, alcance por bodega, productos, bodegas, movimientos, kardex y auditoría. |
| 2 | Proveedores, órdenes de compra, Redis, dashboard y reportes. |
| 3 | RabbitMQ, notificaciones, procesos en segundo plano y auditoría avanzada. |
| 4 | Multiempresa, multitenencia, lotes, vencimientos, números de serie, códigos de barras y aplicación móvil. |
| Frontend | Interfaz React sobre la API estabilizada; su entrega precede a la fase 2 en el plan acordado. |

## Orden de ejecución

| Entrega | Prioridad | Alcance | Dependencia |
|---|---|---|---|
| 1 | P0 | Validación con PostgreSQL y correcciones de autorización e integridad | Punto de partida |
| 2 | P1 | Usuarios, permisos, sesiones, auditoría y consultas operativas | Entrega 1 |
| 3 | P1 | Frontend del MVP | Entrega 2 y contratos de API estabilizados |
| 4 | P2 | Compras, reportes y caché | MVP validado |

P0 identifica la base necesaria para continuar; P1 completa el producto operativo; P2 amplía sus capacidades. El orden no representa una estimación de duración.

## 1. Establecer una validación reproducible con PostgreSQL

- [x] Levantar el entorno local y verificar la configuración de conexión.
- [x] Aplicar migraciones en una base aislada y verificar los datos iniciales.
- [x] Ejecutar la compilación y las pruebas existentes; registrar los resultados actuales.
- [x] Incorporar pruebas de integración mediante la API y una base PostgreSQL aislada.
- [x] Cubrir el flujo login → producto → entrada → salida → transferencia → kardex.
- [x] Verificar persistencia, saldos por bodega y correspondencia entre movimientos y kardex.
- [x] Automatizar compilación y pruebas en integración continua.

**Criterio de finalización:** el flujo completo funciona contra PostgreSQL real y se verifica automáticamente en un entorno reproducible, sin depender de datos de desarrollo.

### Evidencia de cierre — 2026-11-12

- Rama de trabajo: `dev`. SDK oficial actualizado a **11.0.100** y fijado en `global.json`; destino de aplicación `net10.0`, runtime local ASP.NET Core **10.0.15**.
- PostgreSQL local **18.6**, `localhost:5432`, usuario `ozkar`, base `inventario_erp`, creada mediante `scripts/init-db.sh`. Credenciales solo en `.env` ignorado por Git.
- Migración aplicada: `20260609001034_InitialCreate`. Datos iniciales verificados: **21 permisos, 5 roles y 1 administrador**. El script de inicialización y el seeder admiten ejecuciones repetidas.
- `bash scripts/validate.sh`: restauración correcta, compilación Release con **0 advertencias y 0 errores**; **43 pruebas aprobadas, 0 fallidas, 0 omitidas** (Domain 27, Authorization 7, Application 7, API/PostgreSQL 2). Resultados TRX en `TestResults/`.
- Las dos pruebas nuevas comprueban migraciones y seed idempotente, y el flujo completo por HTTP con JWT real, catálogos y dos bodegas. Entrada 100, salida 30 y transferencia 20: saldos finales **50 en origen y 20 en destino**, contrastados con movimientos persistidos y kardex desde nuevos contextos y solicitudes.
- Cada ejecución usa `inventario_erp_test_<uuid>` y elimina esa base al terminar. Se verificó la ausencia de bases temporales al finalizar; no utiliza datos de desarrollo.
- `.github/workflows/validation.yml` automatiza el mismo script para push y pull request con PostgreSQL 16 efímero y publica resultados TRX. El flujo está configurado; su ejecución en GitHub queda para el siguiente push. La ejecución local comprobada fue contra PostgreSQL 18.6.
- El inicio ahora falla con código de salida no cero si la migración o el seed fallan. `--migrate-only` permite inicializar la base sin iniciar HTTP.

Al cerrar el punto 1 quedaron pendientes autorización, auditoría, concurrencia, idempotencia y atomicidad ante errores; su resolución y evidencia se registran en el punto 2.

## 2. Corregir autorización e integridad del inventario

La revisión inicial identificó consultas de stock y kardex sin comprobación del alcance por bodega, serialización de propiedades sensibles como `PasswordHash` en auditoría y ausencia de un control de concurrencia configurado para el stock. Estos problemas están corregidos y cuentan con pruebas de regresión.

- [x] Restringir las consultas de stock y kardex a las bodegas autorizadas para el usuario.
- [x] Aplicar el alcance del usuario a los listados de bodegas y a las consultas sin filtro de bodega.
- [x] Probar acceso autorizado y denegado, incluidos usuarios con acceso global y excepciones Allow/Deny.
- [x] Excluir contraseñas, hashes y tokens de los datos de auditoría.
- [x] Incorporar control de concurrencia para impedir que movimientos simultáneos sobrescriban saldos.
- [x] Definir el comportamiento ante conflictos de concurrencia y verificarlo con PostgreSQL.
- [x] Definir e implementar idempotencia para evitar movimientos duplicados al reintentar solicitudes.
- [x] Definir y probar el tratamiento de productos repetidos dentro de un movimiento.
- [x] Probar ajustes y transferencias con errores, verificando que no persistan cambios parciales.
- [x] Comprobar que stock y kardex permanezcan consistentes después de operaciones simultáneas y reintentos.

**Criterio de finalización:** las pruebas demuestran aislamiento entre bodegas, ausencia de datos sensibles en auditoría, saldos consistentes bajo concurrencia, reintentos sin duplicación y transferencias atómicas.

**Hito de la primera entrega:** completar los pasos 1 y 2 antes de ampliar la administración y construir la interfaz.

### Evidencia de cierre — 2026-11-12

- Rama de trabajo: `dev`. `bash scripts/validate.sh` completado con SDK **11.0.100** y PostgreSQL local **18.6**: restauración correcta, compilación Release con **0 advertencias y 0 errores**, **76 pruebas aprobadas, 0 fallidas, 0 omitidas** (Domain 28, Authorization 7, Application 7, API/PostgreSQL 34). Resultados TRX en `TestResults/`; la suite crea y elimina bases aisladas, sin modificar los datos de desarrollo.
- Stock, kardex y listados de bodegas aplican alcance por usuario; las consultas explícitas fuera de alcance devuelven `403` y las listas sin filtro solo incluyen bodegas autorizadas. La edición de bodegas también comprueba alcance. Se prueban roles, Allow sin rol, Deny con acceso global, usuarios sin alcances y transferencias que requieren acceso a origen y destino. Revocar el alcance impide también reproducir una solicitud confirmada.
- Auditoría usa una lista explícita de propiedades de negocio por entidad. Las pruebas cubren altas, cambios de contraseña, bajas y rotación de tokens, verificando que ni los nombres de campos sensibles ni sus valores entren en los JSON. La migración elimina contraseñas, hashes, tokens y secretos históricos; esta limpieza no se revierte al bajar de versión.
- `Stock.Version` controla escrituras concurrentes, incluidos ajustes al mismo saldo. Los conflictos de versión, creación simultánea de stock y clave duplicada concurrente devuelven `409 ProblemDetails`. También se verifica el tratamiento de errores PostgreSQL `40001` y `40P01` mediante fallos de escritura inyectados en la base: la transferencia completa se revierte y su reintento funciona. No se hacen reintentos automáticos en el servidor.
- Los cuatro tipos de movimiento exigen `idempotencyKey` UUID en el cuerpo. La clave es única por usuario entre todos los tipos, se conserva con el movimiento y no expira. Repetir el mismo comando confirmado devuelve `200` y el movimiento original; cambiar su contenido o tipo devuelve `409`. Duplicados simultáneos pueden recibir `409` y recuperar el resultado al reintentar. Las solicitudes fallidas no consumen la clave; las históricas anteriores a la migración no tienen clave. Los permisos se comprueban antes de devolver resultados previos.
- Productos repetidos se rechazan con `400` y también se impiden en el agregado. Cantidades y costos se validan contra `numeric(18,4)` para evitar redondeos que desajusten stock y kardex. La huella normaliza números equivalentes, como `10` y `10.0`.
- Las pruebas coordinan solicitudes HTTP con conexiones y contextos separados después de leer el stock y antes de guardar. Cubren entradas con stock nuevo y existente, salidas que no pueden consumir más de lo disponible, transferencias con destino nuevo y existente, ajustes al mismo saldo, ajuste frente a salida, duplicados simultáneos y reintentos. Contrastan stock, movimientos, kardex y auditorías desde nuevas solicitudes/contextos.
- Ajustes y transferencias que fallan en una línea posterior no guardan saldos, destinos ni movimientos parciales. Se verifica que la clave pueda utilizarse después de corregir una solicitud fallida. Los conflictos durante `SaveChanges` tampoco dejan auditorías huérfanas.
- Migración incluida: `20261113035606_InventoryIntegrity`. Agrega versiones, idempotencia e índices únicos y `LedgerSequence` para el kardex. Se prueba una actualización desde la migración inicial con datos existentes: conserva el orden histórico, limpia auditoría y continúa la secuencia. Otra prueba confirma saldos correctos aunque el reloj retroceda. El modelo EF coincide con las migraciones.
- El contrato y los reintentos están documentados en el [README](../README.md#contrato-de-movimientos-e-integridad) y las decisiones en [arquitectura.md](arquitectura.md#integridad-de-movimientos--punto-2). El workflow existente ejecuta la misma validación en GitHub; esta evidencia corresponde a la ejecución local.

**Primera entrega completada localmente:** puntos 1 y 2 cerrados. Los puntos 3 y 4 se documentan a continuación. Los puntos 5 y 6 siguen pendientes.

## 3. Completar usuarios, permisos y sesiones

- [x] Implementar edición, activación y desactivación de usuarios.
- [x] Completar las operaciones de dominio y API para asignar y retirar roles.
- [x] Permitir agregar, modificar y retirar excepciones de permisos Allow/Deny.
- [x] Permitir otorgar y retirar acceso a bodegas, incluido el acceso global.
- [x] Validar los identificadores de roles, permisos y bodegas recibidos.
- [x] Agregar consulta del usuario actual y sus permisos efectivos para la interfaz.
- [x] Implementar cierre de sesión y revocación de refresh tokens.
- [x] Definir el alcance de la revocación y el comportamiento de los tokens de acceso después del cierre de sesión.
- [x] Probar rotación, revocación, desactivación de usuarios y aplicación de cambios de permisos.

**Criterio de finalización:** un administrador puede gestionar accesos desde la API; los cambios y la revocación de sesiones se aplican conforme a una política explícita y comprobada mediante pruebas.

### Evidencia de cierre — 2026-11-12

- `bash scripts/validate.sh` completado: **87 pruebas aprobadas, 0 fallidas, 0 omitidas** (Domain 28, Authorization 7, Application 7, API/PostgreSQL 45), compilación Release con **0 advertencias y 0 errores**. Once casos nuevos de administración y sesiones. Verificación de formato .NET sobre archivos modificados y `git diff --check` correctos. Resultados TRX en `TestResults/`.
- Implementado en `dev`, sin dependencias nuevas ni cambios de esquema, infraestructura o CI.
- Edición y estado de usuarios mediante `PUT /users/{id}`; reemplazo completo e idempotente de roles, excepciones y alcances mediante `PUT /users/{id}/access`. Retirar asignaciones usa listas vacías. Las excepciones existentes se actualizan conservando su identidad; EF registra explícitamente las asignaciones nuevas.
- Referencias de roles, permisos y bodegas validadas antes de modificar datos; validación compartida con creación. Datos inválidos devuelven `400`, referencias inexistentes `404` y correos duplicados `409`. La administración de accesos y creación exigen `users.manage` y `roles.manage`.
- Consulta de accesos administrativos, catálogo paginado de permisos y `GET /users/me` con permisos efectivos mediante el motor compartido. Roles, Allow/Deny y alcances se aplican a solicitudes con JWT ya emitidos.
- `POST /auth/logout` revoca todos los refresh tokens del usuario autenticado; `DELETE /users/{id}/sessions` permite revocación administrativa. Desactivar revoca refresh tokens y bloquea los JWT en cada autenticación. Reactivar no restaura refresh tokens revocados.
- Política explícita y contratos documentados en el [README](../README.md#administración-de-usuarios-y-sesiones-punto-3): los JWT siguen vigentes hasta expirar para usuarios activos, incluso después de logout; vuelven a aceptarse al reactivar si aún no vencieron. No se implementa blacklist.
- Pruebas HTTP/PostgreSQL para edición, errores de perfil, asignación y retirada de roles, modificación y retirada de excepciones, alcances individuales/globales, aplicación inmediata de permisos, rechazo de referencias inexistentes, rotación, logout, revocación administrativa, desactivación/reactivación y tokens vencidos. Las bases de pruebas siguen siendo aisladas.

## 4. Completar auditoría y consultas operativas

- [x] Crear una consulta paginada de auditoría protegida por `audit.read`.
- [x] Incorporar filtros útiles para investigar operaciones: usuario, entidad, acción y fechas.
- [x] Registrar cambios en roles, permisos y acceso a bodegas.
- [x] Incorporar listado y detalle de movimientos con sus controles de autorización.
- [x] Paginar las consultas voluminosas y definir un orden estable.
- [x] Conservar el saldo correcto del kardex al filtrar por fechas y paginar resultados.
- [x] Agregar una comprobación de disponibilidad de PostgreSQL, diferenciada de la comprobación de que el proceso está activo.
- [x] Preparar configuración por ambiente y documentar los requisitos para ejecutar la API.

**Criterio de finalización:** se puede rastrear quién realizó cada operación, consultar movimientos y auditoría de forma controlada, y detectar si la API está preparada para atender solicitudes.

### Evidencia de cierre — 2026-11-12

- Trabajo realizado en `/tmp/inventory-point4`, rama `feat/operational-audit`, sobre `dev` (`abdec97`), incorporando el cierre del punto 3. Entrega en `dev`; el worktree temporal se retira tras integrar. No se agregaron dependencias ni se modificó el esquema o las migraciones.
- Validación reproducible: `bash scripts/validate.sh`, cargando la conexión de pruebas del entorno local. Compilación Release con **0 advertencias y 0 errores**; **143 pruebas aprobadas, 0 fallidas, 0 omitidas** (Domain 28, Authorization 7, Application 7, API/configuración 101). De las 101 del proyecto API, **82 usan PostgreSQL real** y 19 validan configuración sin base. Formato .NET de los archivos C# modificados y `git diff --check` correctos. Resultados TRX conservados en `TestResults/` del checkout principal con prefijo `point4_`, junto con `point4-validation.log`, antes de retirar el worktree.
- `GET /api/v1/audit-logs` exige `audit.read`. Se prueban filtros combinados por actor, entidad, acción y fechas inclusivas, JSON de valores anteriores/nuevos, paginación estable con fechas iguales, páginas vacías, autenticación y Deny sobre roles de administrador.
- El interceptor registra altas, cambios y bajas de roles, permisos, asignaciones, excepciones Allow/Deny y alcances. Se verifican el actor y correlación reales de HTTP, las referencias de rol/bodega/usuario, el paso a acceso global y la eliminación de alcances. Un rollback descarta el cambio y su auditoría; las pruebas previas de exclusión de credenciales siguen pasando. Se verifica por HTTP el reemplazo de accesos del punto 3, incluidas altas, modificación de una excepción conservando su ID y bajas de las tres clases de asignaciones.
- `GET /api/v1/inventory/movements` devuelve resúmenes paginados con filtros por producto, bodega, tipo y fechas. `GET /api/v1/inventory/movements/{id}` devuelve el detalle con líneas. Las pruebas cubren acceso global, ambas bodegas, solo origen, solo destino y ningún alcance; las transferencias completas exigen ambas bodegas. Los totales se filtran antes de paginar y los errores devuelven ProblemDetails.
- `GET /api/v2/inventory/{stock,kardex}` reutiliza `PagedResult` y aplica un máximo de 200 filas. Se conservan las respuestas heredadas de v1. Productos comparte el paginador y un desempate estable por UUID. Se validan páginas, límites y fechas UTC. Swagger de Development separa las versiones y sus contratos.
- Kardex pagina en PostgreSQL y calcula el saldo con el historial anterior al filtro. Las pruebas cubren entrada/salida, múltiples ajustes absolutos, transferencia en ambas bodegas, filtros inclusivos, página intermedia, página vacía y reloj regresivo; el ajuste omitido por fecha sigue afectando el saldo. Las pruebas de EF en memoria reproducen la secuencia generada por PostgreSQL sin debilitar sus aserciones.
- `/health` permanece independiente de la base. `/ready` comprueba conexión y migraciones pendientes, con timeout de 3 segundos; las pruebas verifican `200` con la base lista y `503` sin datos internos cuando PostgreSQL está caído o faltan migraciones, manteniendo liveness en `200` y recuperando readiness al aplicar las migraciones.
- Configuración común y archivos para Development, Staging y Production. Se validan conexión, emisor/audiencia, clave JWT y duraciones, además de la pareja opcional de credenciales de bootstrap. No existe clave JWT de respaldo; Staging/Production no migran automáticamente ni crean un administrador de contraseña conocida. `--migrate-only` permite inicialización explícita. Las variables y pasos de ejecución están en `.env.example` y el [README](../README.md#ambientes-y-disponibilidad).
- El workflow existente utiliza el mismo script de validación y las pruebas generan su propia clave JWT, sin cambiar CI. Esta evidencia corresponde a ejecución local. Las consultas y las decisiones están en [README](../README.md#consultas-operativas) y [arquitectura.md](arquitectura.md#auditoría-y-consultas-operativas--punto-4).

**Seguimiento:** se incorporó el punto 3 desde `dev` sin perder su administración de usuarios y sesiones. El contrato de auditoría global y la paginación v2 ya están disponibles para el frontend futuro; los puntos 5 y 6 siguen pendientes. La duplicación previa de reglas de líneas en validadores de movimientos se documentó para un PR independiente.

### OZK-47 — Proteger al último administrador activo (implementado; 2026-11-14)

- Los `PUT /users/{id}` y `PUT /users/{id}/access` serializan mediante el mismo bloqueo advisory en transacción `READ COMMITTED`. Desactivar a un administrador activo o retirarle el rol Administrator devuelve `409` si no queda otro administrador activo; usuarios inactivos no cuentan. La protección se mantiene con solicitudes concurrentes.
- `PUT /users/{id}` devuelve `403` ante auto-desactivación o cambio real del propio email; email equivalente tras trim y normalización a minúsculas sí pasa esa regla. Al editar un administrador se limita tanto el acceso actual como el resultante: roles ∪ Allow debe ser subconjunto de los permisos efectivos del actor, respetando sus Deny, acceso global y alcance de bodegas.
- Perfil, accesos, revocación de refresh tokens y auditoría se guardan atómicamente; los eventos de dominio se despachan después del commit. Sin cambios de esquema ni dependencias.
- Evidencia local del 2026-11-14: **151 pruebas aprobadas, 0 fallidas** (API/PostgreSQL 109, Application 7, Authorization 7, Domain 28), Release con 0 advertencias/errores; formato .NET y `git diff --check` correctos. Los TRX de `TestResults/` registran 109, 7, 7 y 28 aprobadas.
- **Alcance pendiente OZK-46:** controles generales de creación y delegación, además de impedir la autoedición general de accesos. Esta entrega no declara cerrado OZK-46.

## 5. Construir el frontend del MVP

Stack previsto: React, TypeScript y Vite, siguiendo las herramientas de interfaz contempladas en la documentación del proyecto.

- [ ] Crear la base del frontend y su integración con la API.
- [ ] Implementar login, manejo de sesión y cierre de sesión.
- [ ] Incorporar navegación según permisos efectivos y selección de bodega.
- [ ] Crear pantallas de productos y catálogos auxiliares.
- [ ] Crear pantallas de administración de bodegas.
- [ ] Implementar formularios de entradas, salidas, transferencias y ajustes.
- [ ] Incorporar consultas de existencias, listado y detalle de movimientos, y kardex.
- [ ] Incorporar administración de usuarios, roles asignados, permisos excepcionales y acceso a bodegas.
- [ ] Crear la consulta de auditoría.
- [ ] Resolver estados de carga, resultados vacíos, validaciones y errores de la API.
- [ ] Probar los flujos completos desde el navegador con distintos permisos y alcances por bodega.

**Criterio de finalización:** un usuario puede realizar las operaciones diarias de inventario desde la interfaz, respetando los permisos aplicados por el backend.

## 6. Implementar la fase 2

- [ ] Implementar gestión de proveedores.
- [ ] Implementar órdenes de compra y sus estados: borrador, pendiente, aprobada, recibida y cancelada.
- [ ] Definir y validar las transiciones permitidas y los permisos de aprobación y recepción.
- [ ] Implementar recepción de compras que genere entradas de inventario.
- [ ] Garantizar que un reintento de recepción no duplique las entradas ni las existencias.
- [ ] Implementar dashboard y reportes de inventario y compras.
- [ ] Medir las consultas candidatas a caché.
- [ ] Incorporar Redis en las consultas donde las mediciones justifiquen su uso.
- [ ] Definir y probar invalidación y expiración de caché, manteniendo PostgreSQL como fuente de verdad.
- [ ] Probar el ciclo de compra completo y su reflejo en existencias, kardex y reportes.

**Criterio de finalización:** una compra recorre su ciclo completo y actualiza las existencias una sola vez; los reportes reflejan los datos persistidos y la caché no introduce resultados incorrectos.

## Trabajo posterior

- **Reservas:** definir primero el flujo de negocio que requiere reservar y liberar existencias; después implementar sus casos de uso y endpoints.
- **Fase 3:** RabbitMQ, notificaciones, procesos en segundo plano y auditoría avanzada.
- **Fase 4:** multiempresa, multitenencia, lotes, vencimientos, números de serie, códigos de barras y aplicación móvil.

## Seguimiento

- Marcar una tarea como completada únicamente cuando exista evidencia de su implementación y validación.
- Registrar los resultados de cada entrega y actualizar la sección «Estado de partida» de este documento con el estado comprobado y su fecha.
- Revisar dependencias y pendientes al cerrar cada entrega antes de iniciar la siguiente.
