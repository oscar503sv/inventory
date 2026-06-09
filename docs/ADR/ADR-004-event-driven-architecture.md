# ADR-004: Event Driven Architecture

**Status:** Accepted

## Decision
Important business actions generate **domain events**. RabbitMQ distributes events to asynchronous
consumers.

## Domain Events (Phase 1 raises them in-process)
ProductCreated, ProductUpdated, InventoryEntryCreated, InventoryExitCreated,
InventoryTransferCreated, InventoryAdjusted, StockBelowMinimum, PurchaseOrderCreated,
PurchaseOrderApproved, ReportRequested, ReportGenerated, UserCreated, PermissionChanged.

## Consumers
AuditWorker, NotificationWorker, ReportWorker. Future: AnalyticsWorker, IntegrationWorker.

## Phasing
- Phase 1: domain events are raised and dispatched in-process by the persistence layer.
- Phase 3: RabbitMQ broker + consumers are connected.

## Benefits
- Loose coupling
- Scalability
- Extensibility
