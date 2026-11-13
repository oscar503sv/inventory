# Estado y backlog — Inventory ERP

Fecha de planificación: 2026-11-12.

Objetivo: cerrar y validar la fase 1 del backend, construir una interfaz operativa y avanzar después con compras y reportes.

Este documento reúne el estado histórico, los pendientes y la planificación acordada. Los puntos 1 y 2 están implementados y validados localmente; los puntos 3–6 siguen pendientes. Las casillas y la evidencia indican el estado comprobado de cada tarea. Los requisitos se mantienen en [producto.md](producto.md) y las decisiones técnicas en [arquitectura.md](arquitectura.md).

Consolidación documental: 2026-11-12. El contenido de `docs/project.md` se integró aquí; las instrucciones de ejecución están en el [README](../README.md).

## Estado de partida

El estado histórico del 2026-06-08 reporta la base y gran parte del backend de fase 1 implementadas: 41 pruebas aprobadas (Domain 27, Authorization 7, Application 7), compilación sin advertencias ni errores y pruebas de aplicación con EF en memoria. Es evidencia documental anterior; no se volvieron a ejecutar esas comprobaciones al consolidar estos archivos.

La revisión de código del 2026-11-12 identificó los problemas de autorización, auditoría y concurrencia detallados en el paso 2. Se corrigieron y validaron localmente ese mismo día: **76 pruebas aprobadas** (Domain 28, Authorization 7, Application 7, API/PostgreSQL 34), compilación Release sin advertencias ni errores. La validación incluye acceso por bodega, auditoría sin credenciales, idempotencia, errores y concurrencia sobre PostgreSQL real. Quedan pendientes las funciones de los puntos 3–6; esto no equivale al cierre de toda la fase 1 ni a una validación de producción.

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
| Inventory | `POST /inventory/{entries,exits,transfers,adjustments}`, `GET /inventory/{kardex,stock}` |
| Users/Roles | `GET/POST /users`, `GET /roles` |


### Verificación histórica


- ✅ `dotnet build InventoryErp.slnx` — clean.
- ✅ `dotnet test InventoryErp.slnx` — 41 tests green, including an in-memory end-to-end inventory
  flow (entry/exit/transfer → stock + kardex running balance).
- ✅ API boots and serves `/health` (`Healthy`), `/swagger`, returns `401` on protected routes, emits
  the `X-Correlation-Id` header, and degrades to `500 ProblemDetails` (no crash) when the DB is down.
- ✅ **Validado el 2026-11-12:** login → producto → entrada → salida → transferencia → kardex contra PostgreSQL real. Véase la evidencia del punto 1.


### Límites actuales

- Usuarios: creación y listado; administración completa de roles, excepciones y alcances pendiente.
- Auditoría: escritura mediante interceptor con propiedades permitidas y limpieza de campos sensibles históricos; falta la consulta operativa.
- Reservas: `ReservedQuantity` y operaciones Reserve/Release en el dominio, sin casos de uso ni endpoints.
- Sesiones: sin logout ni endpoint de revocación, ni blacklist JWT en Redis.
- `/health` comprueba que el proceso está activo; falta verificar disponibilidad de dependencias.
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

**Primera entrega completada localmente:** puntos 1 y 2 cerrados. El siguiente trabajo corresponde al punto 3; los puntos 3–6 mantienen sus pendientes.

## 3. Completar usuarios, permisos y sesiones

- [ ] Implementar edición, activación y desactivación de usuarios.
- [ ] Completar las operaciones de dominio y API para asignar y retirar roles.
- [ ] Permitir agregar, modificar y retirar excepciones de permisos Allow/Deny.
- [ ] Permitir otorgar y retirar acceso a bodegas, incluido el acceso global.
- [ ] Validar los identificadores de roles, permisos y bodegas recibidos.
- [ ] Agregar consulta del usuario actual y sus permisos efectivos para la interfaz.
- [ ] Implementar cierre de sesión y revocación de refresh tokens.
- [ ] Definir el alcance de la revocación y el comportamiento de los tokens de acceso después del cierre de sesión.
- [ ] Probar rotación, revocación, desactivación de usuarios y aplicación de cambios de permisos.

**Criterio de finalización:** un administrador puede gestionar accesos desde la API; los cambios y la revocación de sesiones se aplican conforme a una política explícita y comprobada mediante pruebas.

## 4. Completar auditoría y consultas operativas

- [ ] Crear una consulta paginada de auditoría protegida por `audit.read`.
- [ ] Incorporar filtros útiles para investigar operaciones: usuario, entidad, acción y fechas.
- [ ] Registrar cambios en roles, permisos y acceso a bodegas.
- [ ] Incorporar listado y detalle de movimientos con sus controles de autorización.
- [ ] Paginar las consultas voluminosas y definir un orden estable.
- [ ] Conservar el saldo correcto del kardex al filtrar por fechas y paginar resultados.
- [ ] Agregar una comprobación de disponibilidad de PostgreSQL, diferenciada de la comprobación de que el proceso está activo.
- [ ] Preparar configuración por ambiente y documentar los requisitos para ejecutar la API.

**Criterio de finalización:** se puede rastrear quién realizó cada operación, consultar movimientos y auditoría de forma controlada, y detectar si la API está preparada para atender solicitudes.

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
