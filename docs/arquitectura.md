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
| `tests/*` | Pruebas de dominio, autorización y aplicación. |

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

- **ADR-001:** el saldo persistido es una proyección del historial. Los manejadores actuales actualizan el stock al guardar movimientos; la validación de consistencia y concurrencia está en el backlog.
- **ADR-003:** el permiso y el alcance por bodega deben comprobarse juntos. La revisión detectó consultas de stock y kardex que todavía no cumplen la comprobación de alcance.
- **ADR-004:** la lista de eventos es el catálogo objetivo e incluye compras y reportes de fases futuras. En la fase 1 el despachador solo registra eventos en el proceso; RabbitMQ y sus consumidores siguen pendientes.
- **ADR-005:** actualmente los refresh tokens se almacenan en PostgreSQL y el rate limiting se ejecuta en la API. Redis está provisionado, pero sus usos previstos no están conectados. La política de revocación se concretará al implementar sesiones.
- Los consumidores previstos son `AuditWorker`, `NotificationWorker` y `ReportWorker`; `AnalyticsWorker` e `IntegrationWorker` quedan para el futuro.
- La disponibilidad de Redis y RabbitMQ se comprobará cuando sus funciones se integren; PostgreSQL es la dependencia operativa inmediata.
