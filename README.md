# Inventory ERP

Enterprise Inventory Management System — Modular Monolith built with **.NET 10**, **PostgreSQL**,
**Redis**, **RabbitMQ** and (future) **React**.

See [`docs/PRD.pdf`](docs/PRD.pdf) for the full product requirements and [`docs/ADR`](docs/ADR) for
the accepted architecture decisions.

## Architecture

Clean Architecture + Modular Monolith. Dependency direction:
`Domain` ← `Application` ← `Persistence` / `Infrastructure` ← `API`.

| Project | Responsibility |
|---|---|
| `Inventory.Domain` | Entities, value objects, domain events, business rules. No dependencies. |
| `Inventory.Application` | Use cases (MediatR), DTOs, validators (FluentValidation), ports. |
| `Inventory.Persistence` | EF Core `DbContext`, configurations, repositories, migrations (PostgreSQL). |
| `Inventory.Infrastructure` | JWT, hashing, Redis, RabbitMQ publisher, authorization engine, audit. |
| `Inventory.API` | Controllers, middleware, DI composition, Swagger, Serilog, CorrelationId. |
| `Shared.Contracts` | Shared DTOs / contracts. |
| `Shared.Events` | Domain / integration event contracts. |
| `Shared.Authorization` | Permission constants, policies/handlers, effective-permission engine. |

**Bounded contexts (Phase 1):** Identity, Catalog, Warehouses, Inventory, Audit.

## Project status

Foundation + **Phase 1 backend** implemented and tested (41 tests, clean build). Phases 2–4 and the
frontend are not started. See **[docs/project.md](docs/project.md)** for the detailed status of what
is done and what is pending.

## Core principles

- **ADR-001** Stock is never edited directly — only through inventory movements (Entry/Exit/Transfer/Adjustment).
- **ADR-002** Stock is calculated per warehouse; global stock = sum of warehouses.
- **ADR-003** Authorization = `RolePermissions ∪ UserAllow − UserDeny`, scoped per warehouse.
- **ADR-004** Important actions raise domain events (RabbitMQ in Phase 3).
- **ADR-005** PostgreSQL is the source of truth; Redis is cache only.

## Getting started

Prerequisites: .NET SDK 10, Docker Desktop.

```bash
# 1. Start infrastructure
docker compose up -d

# 2. Apply database migrations (from repo root)
dotnet ef database update --project src/Inventory.Persistence --startup-project src/Inventory.API

# 3. Run the API
dotnet run --project src/Inventory.API

# 4. Build / test
dotnet build InventoryErp.slnx
dotnet test InventoryErp.slnx
```

Infrastructure ports: PostgreSQL `5433` (host) → `5432` (container, mapped to avoid clashing with a
locally-installed PostgreSQL), Redis `6379`, RabbitMQ `5672` (management UI `15672`).

## Roadmap

- **Phase 1 (current):** Auth, Users, Roles, Permissions, Warehouse Scopes, Products, Warehouses, Inventory Movements, Kardex, Audit.
- **Phase 2:** Procurement (Suppliers, Purchase Orders), Reporting/Dashboard, active Redis cache.
- **Phase 3:** RabbitMQ consumers, notifications, advanced audit.
- **Phase 4:** Multi-Company/Multi-Tenant, Lots, Expiration, Serial Numbers, Barcode, Mobile.
- **Frontend:** React/Vite/TanStack/shadcn (planned after the API stabilizes).
