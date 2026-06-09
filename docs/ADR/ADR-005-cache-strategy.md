# ADR-005: Cache Strategy

**Status:** Accepted

## Decision
Redis SHALL be used only as a cache and auxiliary storage. **PostgreSQL remains the source of truth.**

## Redis is used for
- Catalog cache
- Dashboard cache
- Permission cache
- Refresh tokens
- JWT blacklist
- Rate limiting
- Frequently accessed reports

## Constraint
Redis SHALL NOT store authoritative inventory data.

## Phasing
Phase 1 keeps PostgreSQL as the only store. Active Redis caching is wired in Phase 2.
