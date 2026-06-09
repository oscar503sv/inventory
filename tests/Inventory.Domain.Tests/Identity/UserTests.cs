using FluentAssertions;
using Inventory.Domain.Common;
using Inventory.Domain.Identity;
using Inventory.Domain.Identity.Events;

namespace Inventory.Domain.Tests.Identity;

public class UserTests
{
    private static User Create() => User.Create("Admin@Test.com", "admin", "Admin", "hash");

    [Fact]
    public void Create_normalizes_email_and_raises_UserCreated()
    {
        var user = Create();

        user.Email.Should().Be("admin@test.com");
        user.IsActive.Should().BeTrue();
        user.DomainEvents.Should().ContainSingle(e => e is UserCreatedDomainEvent);
    }

    [Fact]
    public void Create_without_password_hash_is_rejected()
    {
        var act = () => User.Create("a@b.com", "u", "n", " ");

        act.Should().Throw<DomainException>().WithMessage("*Password hash*");
    }

    [Fact]
    public void AssignRole_is_idempotent()
    {
        var user = Create();
        var roleId = Guid.NewGuid();

        user.AssignRole(roleId);
        user.AssignRole(roleId);

        user.Roles.Should().ContainSingle(r => r.RoleId == roleId);
    }

    [Fact]
    public void SetOverride_replaces_previous_override_for_same_permission()
    {
        var user = Create();
        var permissionId = Guid.NewGuid();

        user.SetOverride(permissionId, OverrideType.Allow);
        user.SetOverride(permissionId, OverrideType.Deny);

        user.Overrides.Should().ContainSingle(o => o.PermissionId == permissionId)
            .Which.OverrideType.Should().Be(OverrideType.Deny);
    }

    [Fact]
    public void GrantAllWarehousesAccess_clears_specific_scopes()
    {
        var user = Create();
        user.GrantWarehouseScope(Guid.NewGuid());

        user.GrantAllWarehousesAccess();

        user.HasAllWarehousesAccess.Should().BeTrue();
        user.Scopes.Should().BeEmpty();
    }

    [Fact]
    public void GrantWarehouseScope_is_ignored_when_user_has_all_access()
    {
        var user = Create();
        user.GrantAllWarehousesAccess();

        user.GrantWarehouseScope(Guid.NewGuid());

        user.Scopes.Should().BeEmpty();
    }
}
