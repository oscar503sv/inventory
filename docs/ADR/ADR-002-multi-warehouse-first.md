# ADR-002: Multi-Warehouse First

**Status:** Accepted

## Decision
Multi-warehouse support is mandatory from version 1.

## Rules
- A product stock is calculated **per warehouse**.
- Warehouse inventory is the primary inventory unit.
- Global stock is the sum across warehouses.

### Example
```
Warehouse A -> 100
Warehouse B -> 50
Global Stock -> 150
```

## Benefits
- Enterprise readiness
- Easier future expansion
- Avoids costly migrations
