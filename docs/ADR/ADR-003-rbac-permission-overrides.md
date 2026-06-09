# ADR-003: RBAC + Permission Overrides

**Status:** Accepted

## Decision
The authorization model SHALL combine:
- Roles
- Role Permissions
- User Permission Overrides (Allow / Deny)
- Warehouse Scopes

## Authorization Structure
```
User
 ├─ Roles
 ├─ Allow Permissions
 ├─ Deny Permissions
 └─ Warehouse Scopes
```

## Effective Permission Formula
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

## Benefits
- Avoid role explosion
- Greater flexibility
- Easier administration
