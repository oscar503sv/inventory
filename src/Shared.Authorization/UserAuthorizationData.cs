namespace Shared.Authorization;

/// <summary>
/// The raw authorization inputs for a user (ADR-003): permissions granted by roles, per-user
/// allow/deny overrides, and warehouse scoping. The effective permission set is derived by
/// <see cref="PermissionEngine"/>.
/// </summary>
public sealed record UserAuthorizationData(
    IReadOnlySet<string> RolePermissions,
    IReadOnlySet<string> AllowOverrides,
    IReadOnlySet<string> DenyOverrides,
    bool HasAllWarehousesAccess,
    IReadOnlySet<Guid> WarehouseScopes)
{
    public static UserAuthorizationData Empty { get; } = new(
        new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), false, new HashSet<Guid>());
}
