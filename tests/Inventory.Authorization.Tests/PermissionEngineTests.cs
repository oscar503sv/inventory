using FluentAssertions;
using Shared.Authorization;

namespace Inventory.Authorization.Tests;

public class PermissionEngineTests
{
    private static UserAuthorizationData Data(
        string[]? roles = null,
        string[]? allow = null,
        string[]? deny = null,
        bool allWarehouses = false,
        Guid[]? scopes = null) => new(
            RolePermissions: new HashSet<string>(roles ?? []),
            AllowOverrides: new HashSet<string>(allow ?? []),
            DenyOverrides: new HashSet<string>(deny ?? []),
            HasAllWarehousesAccess: allWarehouses,
            WarehouseScopes: new HashSet<Guid>(scopes ?? []));

    [Fact]
    public void Role_permissions_are_granted()
    {
        var data = Data(roles: [Permissions.Products.Read]);

        PermissionEngine.HasPermission(data, Permissions.Products.Read).Should().BeTrue();
        PermissionEngine.HasPermission(data, Permissions.Products.Delete).Should().BeFalse();
    }

    [Fact]
    public void Allow_override_grants_permission_not_in_any_role()
    {
        var data = Data(allow: [Permissions.Inventory.AdjustApprove]);

        PermissionEngine.HasPermission(data, Permissions.Inventory.AdjustApprove).Should().BeTrue();
    }

    [Fact]
    public void Deny_override_wins_over_role_grant()
    {
        var data = Data(roles: [Permissions.Products.Delete], deny: [Permissions.Products.Delete]);

        PermissionEngine.HasPermission(data, Permissions.Products.Delete).Should().BeFalse();
    }

    [Fact]
    public void Deny_override_wins_over_allow_override()
    {
        var data = Data(allow: [Permissions.Reports.Export], deny: [Permissions.Reports.Export]);

        PermissionEngine.HasPermission(data, Permissions.Reports.Export).Should().BeFalse();
    }

    [Fact]
    public void EffectivePermissions_is_roles_union_allow_minus_deny()
    {
        var data = Data(
            roles: [Permissions.Products.Read, Permissions.Products.Delete],
            allow: [Permissions.Reports.Export],
            deny: [Permissions.Products.Delete]);

        var effective = PermissionEngine.ComputeEffectivePermissions(data);

        effective.Should().BeEquivalentTo(new[]
        {
            Permissions.Products.Read,
            Permissions.Reports.Export
        });
    }

    [Fact]
    public void User_with_all_warehouses_access_can_access_any_warehouse()
    {
        var data = Data(allWarehouses: true);

        PermissionEngine.CanAccessWarehouse(data, Guid.NewGuid()).Should().BeTrue();
    }

    [Fact]
    public void Scoped_user_can_access_only_scoped_warehouses()
    {
        var w1 = Guid.NewGuid();
        var w2 = Guid.NewGuid();
        var other = Guid.NewGuid();
        var data = Data(scopes: [w1, w2]);

        PermissionEngine.CanAccessWarehouse(data, w1).Should().BeTrue();
        PermissionEngine.CanAccessWarehouse(data, w2).Should().BeTrue();
        PermissionEngine.CanAccessWarehouse(data, other).Should().BeFalse();
    }
}
