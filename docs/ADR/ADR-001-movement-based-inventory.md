# ADR-001: Movement-Based Inventory

**Status:** Accepted

## Decision
Inventory quantities SHALL NEVER be edited directly. All inventory changes must be generated
through **inventory movements**.

### Movement Types
- Entry
- Exit
- Transfer
- Adjustment

## Benefits
- Complete auditability
- Historical reconstruction
- Inventory consistency
- Traceability

## Consequences
- `Stock` is a derived/projected value, never an input. It is recalculated from movements.
- Historical movements are immutable (cannot be deleted).
- Every movement requires a user and a timestamp.
