# Arquitectura — Inventory ERP

Consolidación: 2026-11-12. Este documento reúne la arquitectura y las cinco decisiones aceptadas anteriormente en `docs/ADR/`, conservando sus identificadores y su contenido. Los requisitos funcionales están en [producto.md](producto.md); el estado real y la planificación están en [backlog.md](backlog.md).

## Estructura de la solución

Monolito modular con Clean Architecture. Dirección de dependencias:

`Domain` ← `Application` ← `Persistence` / `Infrastructure` ← `API`.

Solución: `InventoryErp.slnx`. Backend: C# y .NET 10.

| Proyecto | Responsabilidad |
|---|---|
| `Inventory.Domain` | Entidades, reglas de negocio y eventos de dominio; sin dependencias externas. |
| `Inventory.Application` | Casos de uso con MediatR, validadores FluentValidation y abstracciones. |
| `Inventory.Persistence` | EF Core, PostgreSQL, configuraciones, migraciones, datos iniciales e interceptor de auditoría. |
| `Inventory.Infrastructure` | JWT, hashing, proveedor de autorización, numeración de movimientos y despacho de eventos; futuras integraciones Redis y RabbitMQ. |
| `Inventory.API` | Controladores, middleware, autenticación, composición de dependencias, Swagger y observabilidad. |
| `Shared.Contracts` | DTO y contratos compartidos. |
| `Shared.Events` | Espacio reservado para contratos de integración de la fase 3. |
| `Shared.Authorization` | Códigos de permisos, motor de permisos efectivos y políticas de ASP.NET Core. |
| `tests/*` | Pruebas de dominio, autorización, aplicación, API/PostgreSQL y configuración. |

## Contextos del negocio

| Contexto | Responsabilidades |
|---|---|
| Identity | Usuarios, roles, permisos, autenticación y autorización. |
| Catalog | Productos, categorías, marcas y unidades de medida. |
| Warehouses | Bodegas, alcance por bodega y transferencias. |
| Inventory | Existencias, movimientos, ajustes, reservas y kardex. |
| Procurement | Proveedores, órdenes de compra y recepciones. |
| Reporting | Reportes de inventario, operativos y exportaciones. |
| Audit | Registros de auditoría, eventos de dominio y actividad de usuarios. |

Identity, Catalog, Warehouses, Inventory y Audit corresponden al núcleo de la fase 1. La presencia de un contexto en el diseño no implica que todas sus funciones estén implementadas.

## Stack previsto

| Área | Tecnologías |
|---|---|
| Backend | ASP.NET Core 10, C#, Entity Framework Core, FluentValidation y MediatR. El PRD considera MediatR y AutoMapper opcionales; MediatR ya se utiliza. |
| Persistencia | PostgreSQL como fuente de verdad; Redis para caché y almacenamiento auxiliar. |
| Mensajería | RabbitMQ. |
| Autenticación | JWT y refresh tokens. |
| Frontend | React, TypeScript, Vite, TanStack Query, TanStack Table, React Hook Form, Zod y shadcn/ui. |
| Observabilidad | Serilog, OpenTelemetry, Prometheus y Grafana. |
| Infraestructura | Docker y Docker Compose. |
| Pruebas | xUnit y FluentAssertions. |

## Decisiones aceptadas

Los ADR siguientes expresan el diseño objetivo. Se conserva el texto original; las aclaraciones de implementación posteriores evitan confundir ese diseño con el estado actual.

## ADR-001: Movement-Based Inventory

**Status:** Accepted

### Decision
Inventory quantities SHALL NEVER be edited directly. All inventory changes must be generated
through **inventory movements**.

#### Movement Types
- Entry
- Exit
- Transfer
- Adjustment

### Benefits
- Complete auditability
- Historical reconstruction
- Inventory consistency
- Traceability

### Consequences
- `Stock` is a derived/projected value, never an input. It is recalculated from movements.
- Historical movements are immutable (cannot be deleted).
- Every movement requires a user and a timestamp.

## ADR-002: Multi-Warehouse First

**Status:** Accepted

### Decision
Multi-warehouse support is mandatory from version 1.

### Rules
- A product stock is calculated **per warehouse**.
- Warehouse inventory is the primary inventory unit.
- Global stock is the sum across warehouses.

#### Example
```
Warehouse A -> 100
Warehouse B -> 50
Global Stock -> 150
```

### Benefits
- Enterprise readiness
- Easier future expansion
- Avoids costly migrations

## ADR-003: RBAC + Permission Overrides

**Status:** Accepted

### Decision
The authorization model SHALL combine:
- Roles
- Role Permissions
- User Permission Overrides (Allow / Deny)
- Warehouse Scopes

### Authorization Structure
```
User
 ├─ Roles
 ├─ Allow Permissions
 ├─ Deny Permissions
 └─ Warehouse Scopes
```

### Effective Permission Formula
```
EffectivePermissions = RolePermissions
                       ∪ UserAllowPermissions
                       − UserDenyPermissions
```

Additionally, the authorization engine MUST validate:
- Permission
- Warehouse Scope

### Protección del último administrador activo (OZK-47, 2026-11-14)

Los `PUT` de perfil y accesos de usuarios comparten un bloqueo advisory transaccional y ejecutan en `READ COMMITTED`. Así, desactivar un usuario con rol Administrator o quitarle el rol requiere que quede otro usuario activo con ese rol; las operaciones concurrentes no pueden eliminar simultáneamente a los dos últimos. El perfil también prohíbe la auto-desactivación y el cambio del propio email (`403`).

Al modificar un administrador, tanto su acceso actual como el resultante deben caber en los permisos efectivos y alcance del actor. Para el techo se consideran roles más permisos Allow explícitos; los Deny del actor prevalecen. El acceso global requiere actor global y las bodegas asignadas deben estar dentro de su alcance. Los cambios, revocaciones de sesión y auditoría son atómicos; los eventos de dominio se despachan después del commit. Estos controles se aplican junto con la validación general de creación y delegación y la prohibición de autoedición de accesos de OZK-46. La autorización del actor se recarga después de adquirir el bloqueo para no usar permisos anteriores a la espera.

A user with `inventory.read` scoped to Warehouses {1, 2} may only read those warehouses; a user
scoped to "All Warehouses" may read any.

### Benefits
- Avoid role explosion
- Greater flexibility
- Easier administration

## ADR-004: Event Driven Architecture

**Status:** Accepted

### Decision
Important business actions generate **domain events**. RabbitMQ distributes events to asynchronous
consumers.

### Domain Events (Phase 1 raises them in-process)
ProductCreated, ProductUpdated, InventoryEntryCreated, InventoryExitCreated,
InventoryTransferCreated, InventoryAdjusted, StockBelowMinimum, PurchaseOrderCreated,
PurchaseOrderApproved, ReportRequested, ReportGenerated, UserCreated, PermissionChanged.

### Consumers
AuditWorker, NotificationWorker, ReportWorker. Future: AnalyticsWorker, IntegrationWorker.

### Phasing
- Phase 1: domain events are raised and dispatched in-process by the persistence layer.
- Phase 3: RabbitMQ broker + consumers are connected.

### Benefits
- Loose coupling
- Scalability
- Extensibility

## ADR-005: Cache Strategy

**Status:** Accepted

### Decision
Redis SHALL be used only as a cache and auxiliary storage. **PostgreSQL remains the source of truth.**

### Redis is used for
- Catalog cache
- Dashboard cache
- Permission cache
- Refresh tokens
- JWT blacklist
- Rate limiting
- Frequently accessed reports

### Constraint
Redis SHALL NOT store authoritative inventory data.

### Phasing
Phase 1 keeps PostgreSQL as the only store. Active Redis caching is wired in Phase 2.

## Aplicación por etapas y aclaraciones

- **ADR-001:** el saldo persistido es una proyección del historial. Los manejadores actuales actualizan el stock al guardar movimientos; el punto 2 incorpora versiones optimistas de stock, idempotencia por usuario y pruebas de consistencia con PostgreSQL. Un único `SaveChanges` confirma stock, movimiento, líneas y auditoría; cualquier conflicto revierte la operación y devuelve `409`. No hay reintentos automáticos en el servidor.
- **ADR-003:** el permiso y el alcance por bodega deben comprobarse juntos. Stock, kardex y listados de bodegas aplican el alcance. Las consultas explícitas fuera de alcance devuelven `403`; las listas sin filtro se restringen a las bodegas autorizadas. Los reintentos vuelven a comprobar autorización antes de devolver un movimiento existente.
- **ADR-004:** la lista de eventos es el catálogo objetivo e incluye compras y reportes de fases futuras. En la fase 1 el despachador solo registra eventos en el proceso; RabbitMQ y sus consumidores siguen pendientes.
- **ADR-005:** actualmente los refresh tokens se almacenan en PostgreSQL y el rate limiting se ejecuta en la API. Redis está provisionado, pero sus usos previstos no están conectados. La política de revocación se concretará al implementar sesiones.
- Los consumidores previstos son `AuditWorker`, `NotificationWorker` y `ReportWorker`; `AnalyticsWorker` e `IntegrationWorker` quedan para el futuro.
- La disponibilidad de Redis y RabbitMQ se comprobará cuando sus funciones se integren; PostgreSQL es la dependencia operativa inmediata.

## Integridad de movimientos — punto 2

- `Stock.Version` es un token de concurrencia que avanza en cada operación del dominio, incluso un ajuste al mismo saldo. EF comprueba la versión original al escribir. La unicidad producto/bodega también protege la creación simultánea de stock.
- Cada comando exige una clave UUID. El movimiento conserva la clave y una huella SHA-256 del tipo y contenido del comando. El índice único `(CreatedBy, IdempotencyKey)` protege también contra solicitudes duplicadas concurrentes. La clave, el movimiento y sus efectos se confirman en la misma transacción de `SaveChanges`.
- Ante una versión obsoleta, creación simultánea de stock, clave concurrente, deadlock o fallo de serialización de escritura, se devuelve `409` sin guardar cambios parciales. Se descarta el estado seguido por el contexto; el cliente reintenta toda la solicitud con la misma clave. Una clave confirmada con contenido diferente también devuelve `409`.
- `LedgerSequence`, generada por PostgreSQL, determina el orden del kardex. Los movimientos que dependen de un saldo confirmado reciben una secuencia posterior; los que leyeron una versión obsoleta fallan. Los huecos por transacciones revertidas son normales. Las fechas siguen siendo informativas y se conservan los saldos previos al aplicar filtros de fecha.
- Los productos repetidos se rechazan tanto en validación como en el agregado. No se suman automáticamente, especialmente porque un ajuste representa una cantidad absoluta. Las cantidades y costos se validan contra la precisión persistida `numeric(18,4)`; la huella normaliza representaciones numéricas equivalentes como `10` y `10.0`.
- Auditoría serializa solo propiedades de negocio autorizadas explícitamente por entidad. La migración elimina campos sensibles de registros históricos como excepción de seguridad a su inmutabilidad; una reversión de esquema no restaura esas credenciales.

## Auditoría y consultas operativas — punto 4

- La auditoría incorpora `Permission`, `UserRole`, `RolePermission`, `UserPermissionOverride` y `WarehouseScope`, además de las entidades existentes. Las propiedades siguen una lista explícita; se incluye la clave primaria para identificar el objeto o asignación. No hay cambios de esquema ni dependencias nuevas. El actor procede de `ICurrentUser`, con fecha, correlación y valores anteriores/nuevos dentro del mismo `SaveChanges` que guarda el cambio. Las asignaciones eliminadas y el acceso global quedan registrados. Las operaciones de administración incorporadas en el punto 3 utilizan el mismo interceptor.
- `GET /api/v1/audit-logs` exige `audit.read`, un permiso de investigación global que no se limita por bodega. Filtra actor, entidad, acción y fechas; devuelve JSON de valores como objetos, no como cadenas JSON anidadas. No hay modificaciones ni borrados de auditoría expuestos por la API.
- Listado y detalle de movimientos exigen `inventory.read`. Una transferencia completa exige acceso a ambas bodegas. El filtro de visibilidad se aplica antes de contar o paginar, evitando revelar totales o destinos fuera de alcance. El detalle devuelve `403` para un movimiento existente fuera de alcance y `404` para un UUID inexistente.
- Las consultas nuevas reutilizan `PagedResult<T>` y `QueryPaging` (20 filas por defecto, máximo 200), con validación común de páginas y rangos UTC. Productos reutiliza el mismo paginador y añade el UUID como desempate. El detalle carga las líneas con eager loading; los listados proyectan únicamente los campos necesarios.
- Las respuestas paginadas de stock/kardex se publican en `/api/v2/inventory` para conservar los arreglos de v1. Ambas versiones reutilizan el filtro de stock y el cálculo del kardex; Swagger separa los documentos por versión.
- El cálculo del kardex consulta el último ajuste anterior a cada fila y suma los deltas posteriores a ese ajuste. Las subconsultas consultan el historial sin restringirlo por fechas ni página; la selección de filas se pagina en PostgreSQL. Así se conserva el saldo de apertura con ajustes absolutos, fechas iguales o un reloj que retrocede. La versión heredada reutiliza el mismo cálculo.
- La paginación es por desplazamiento, con un orden determinista: auditoría por fecha/UUID descendentes, movimientos por secuencia descendente, stock por bodega/producto y kardex por secuencia ascendente. No ofrece una instantánea entre solicitudes independientes que ocurran mientras se insertan datos.
- `/health` excluye las comprobaciones de dependencias. `/ready` comprueba conexión y migraciones aplicadas con timeout de 3 segundos, y emite `503 ProblemDetails` sin detalles de conexión si no está preparada la base. Ambos endpoints son públicos para probes. Los errores HTTP sin cuerpo y los errores de aplicación comparten el escritor de ProblemDetails con correlación.
- La configuración común no contiene una clave JWT ni credenciales de RabbitMQ. Development/Testing inicializan la base por defecto; Staging/Production requieren inicialización deliberada y no crean un administrador conocido. Las credenciales iniciales de estos últimos se reciben por entorno. La conexión, JWT y credenciales opcionales se validan antes de construir el host. Los logs operativos usan JSON con el contexto de correlación existente.

Duplicación previa observada, fuera del alcance del punto 4: los cuatro validadores de movimientos repiten reglas de líneas. Su extracción debe tratarse en un PR enfocado, sin mezclarla con estas consultas.
