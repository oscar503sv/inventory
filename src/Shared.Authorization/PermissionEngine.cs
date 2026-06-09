namespace Shared.Authorization;

/// <summary>
/// Pure, side-effect-free authorization logic (ADR-003).
/// <code>EffectivePermissions = RolePermissions ∪ AllowOverrides − DenyOverrides</code>
/// Deny always wins. The engine also validates warehouse scope.
/// </summary>
public static class PermissionEngine
{
    public static IReadOnlySet<string> ComputeEffectivePermissions(UserAuthorizationData data)
    {
        var effective = new HashSet<string>(data.RolePermissions, StringComparer.OrdinalIgnoreCase);
        effective.UnionWith(data.AllowOverrides);
        effective.ExceptWith(data.DenyOverrides);
        return effective;
    }

    public static bool HasPermission(UserAuthorizationData data, string permission)
    {
        // Deny wins regardless of role or allow grants.
        if (data.DenyOverrides.Contains(permission))
            return false;

        return data.RolePermissions.Contains(permission) || data.AllowOverrides.Contains(permission);
    }

    public static bool CanAccessWarehouse(UserAuthorizationData data, Guid warehouseId)
        => data.HasAllWarehousesAccess || data.WarehouseScopes.Contains(warehouseId);
}
