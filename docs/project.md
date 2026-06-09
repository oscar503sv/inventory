# Project Status — Inventory ERP

_Last updated: 2026-06-08_

Status of the implementation against [`PRD.pdf`](PRD.pdf) and the [ADRs](ADR). The **Foundation +
Phase 1 backend** is implemented, tested and committed. Phases 2–4 and the frontend are not started.

| | |
|---|---|
| **Stack** | .NET 10 · ASP.NET Core · EF Core · PostgreSQL · (Redis, RabbitMQ provisioned, not yet used) |
| **Style** | Modular Monolith + Clean Architecture |
| **Solution** | `InventoryErp.slnx` (new XML solution format) |
| **Tests** | 41 passing (Domain 27 · Authorization 7 · Application 7) |
| **Build** | Clean (0 warnings, 0 errors) |

---

## Architecture snapshot

Dependency direction: `Domain` ← `Application` ← `Persistence` / `Infrastructure` ← `API`.

| Project | Responsibility | Status |
|---|---|---|
| `Inventory.Domain` | Entities, value objects, domain events, business rules | ✅ Phase 1 |
| `Inventory.Application` | Use cases (MediatR), validators (FluentValidation), ports | ✅ Phase 1 |
| `Inventory.Persistence` | EF Core `DbContext`, configs, interceptor, migration, seeder | ✅ Phase 1 |
| `Inventory.Infrastructure` | JWT, hashing, auth provider, movement numbering, event dispatch | ✅ Phase 1 |
| `Inventory.API` | Controllers, middleware, DI, Swagger, observability | ✅ Phase 1 |
| `Shared.Contracts` | Shared DTOs | ✅ |
| `Shared.Events` | Integration event contracts | ⛔ placeholder (Phase 3) |
| `Shared.Authorization` | Permission engine, constants, ASP.NET policy/handler | ✅ Phase 1 |
| `tests/*` | Domain / Authorization / Application test suites | ✅ |

---

## Implemented (Phase 1)

### Identity & Auth
- ✅ JWT login + refresh-token rotation (hashed refresh tokens).
- ✅ RBAC + per-user Allow/Deny overrides + warehouse scopes — engine
  `EffectivePermissions = Roles ∪ Allow − Deny` with warehouse-scope validation (ADR-003).
- ✅ `[RequirePermission(...)]` policy provider/handler over business-oriented permission codes.
- ✅ Seeder: full permission catalog, 5 default roles, initial admin user.

### Catalog
- ✅ Products: create, update, soft-delete (deactivate), get-by-id, paged/searchable list.
- ✅ Categories, Brands, Units of Measure: create + list.

### Warehouses
- ✅ Create, update, list (multi-warehouse first — ADR-002).

### Inventory (core)
- ✅ Movement-based stock (ADR-001): Entry, Exit, Transfer, Adjustment — stock is never edited directly.
- ✅ Per-warehouse `Stock` with `AvailableQuantity = Quantity − ReservedQuantity`; negative stock rejected.
- ✅ Adjustments require justification; transfers are atomic across two warehouses.
- ✅ Kardex (stock ledger) with running balance; stock query.
- ✅ `StockBelowMinimum` domain event raised when stock drops under the product minimum.

### Audit
- ✅ Immutable `AuditLog` written by a `SaveChanges` interceptor (user, timestamp, entity, action,
  old/new values as `jsonb`, IP, correlation id) for critical entities.

### Cross-cutting
- ✅ CorrelationId middleware (every request), RFC 7807 ProblemDetails error handling.
- ✅ JWT bearer auth, fixed-window rate limiting, Swagger with bearer button, `/health`.
- ✅ Serilog structured logging, OpenTelemetry tracing, Prometheus metrics at `/metrics`.
- ✅ In-process domain-event dispatch after commit (the seam for the future RabbitMQ publisher).

### API surface (`/api/v1`)
| Area | Endpoints |
|---|---|
| Auth | `POST /auth/login`, `POST /auth/refresh` |
| Products | `GET/POST /products`, `GET/PUT/DELETE /products/{id}` |
| Catalog | `GET/POST /categories`, `/brands`, `/units-of-measure` |
| Warehouses | `GET/POST /warehouses`, `PUT /warehouses/{id}` |
| Inventory | `POST /inventory/{entries,exits,transfers,adjustments}`, `GET /inventory/{kardex,stock}` |
| Users/Roles | `GET/POST /users`, `GET /roles` |

---

## Verification status

- ✅ `dotnet build InventoryErp.slnx` — clean.
- ✅ `dotnet test InventoryErp.slnx` — 41 tests green, including an in-memory end-to-end inventory
  flow (entry/exit/transfer → stock + kardex running balance).
- ✅ API boots and serves `/health` (`Healthy`), `/swagger`, returns `401` on protected routes, emits
  the `X-Correlation-Id` header, and degrades to `500 ProblemDetails` (no crash) when the DB is down.
- ⏳ **Pending:** full end-to-end run against a real PostgreSQL (login → product → entry → kardex).
  Requires Docker, which was still finishing installation and needs a reboot.

---

## Environment notes

- The compose PostgreSQL is published on host port **5433** → container 5432, because a
  **locally-installed PostgreSQL already owns 5432**. Connection strings already point at 5433.
- Default admin (seeded): **`admin@inventory.local` / `Admin#12345`** (all-warehouses access).
- Run the API with `--no-launch-profile` and `ASPNETCORE_URLS` set, otherwise `launchSettings.json`
  overrides the port.

```bash
docker compose up -d
dotnet ef database update --project src/Inventory.Persistence --startup-project src/Inventory.API
dotnet run --project src/Inventory.API --no-launch-profile   # http://localhost:5080/swagger
```

---

## Pending / not implemented

### Phase 1 gaps (nice-to-finish before moving on)
- ⛔ User management is partial: only create + list. Missing update, activate/deactivate, and API
  endpoints to assign roles / set Allow-Deny overrides / manage warehouse scopes (domain supports them).
- ⛔ No audit read endpoint, although the `audit.read` permission and `AuditLog` data exist.
- ⛔ Reservations (`ReservedQuantity`, Reserve/Release) modelled in the domain but no use case/endpoint.
- ⛔ No logout / refresh-token revocation endpoint and no JWT blacklist (Redis).
- ⛔ Health check is liveness-only — no readiness probes for PostgreSQL/Redis/RabbitMQ.
- ⛔ Integration tests run on EF in-memory only; real PostgreSQL integration tests are pending.

### Phase 2
- ⛔ Procurement: Suppliers, Purchase Orders (Draft → Pending → Approved → Received → Cancelled;
  receiving generates inventory entries).
- ⛔ Reporting / Dashboard.
- ⛔ Active Redis caching (catalog, permissions, dashboard, reports).

### Phase 3
- ⛔ RabbitMQ broker + consumers (AuditWorker, NotificationWorker, ReportWorker). Today the
  domain-event dispatcher is an in-process logging stub.
- ⛔ Notifications and advanced audit.

### Phase 4
- ⛔ Multi-Company / Multi-Tenant, Lots, Expiration dates, Serial numbers, Barcode support, Mobile app.

### Frontend
- ⛔ React / Vite / TypeScript / TanStack Query+Table / React Hook Form / Zod / shadcn — not started.

---

## Suggested next steps

1. After the reboot, bring up Docker and run the full DB end-to-end verification.
2. Close the Phase 1 gaps (user/role administration endpoints, audit read, token revocation).
3. Then choose a direction: start the **frontend** on the stable API, or begin **Phase 2** (Procurement + Redis cache).
