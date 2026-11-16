using Inventory.Application.Features.Users;
using Inventory.Domain.Identity;

namespace Inventory.Application.Tests.Validators;

public sealed class UserCommandValidatorTests
{
    private static CreateUserCommand ValidCreate() => new("valid@test.local", "user", "Test User", "Password#123", [], false, []);
    private static UpdateUserAccessCommand ValidAccess() => new(Guid.NewGuid(), [], [], false, []);

    [Theory]
    [InlineData("empty-email", "Email")]
    [InlineData("invalid-email", "Email")]
    [InlineData("empty-name", "UserName")]
    [InlineData("long-name", "UserName")]
    [InlineData("empty-password", "Password")]
    [InlineData("short-password", "Password")]
    public void Create_rejects_invalid_credentials(string scenario, string field)
    {
        var valid = ValidCreate();
        var command = scenario switch
        {
            "empty-email" => valid with { Email = "" },
            "invalid-email" => valid with { Email = "invalid" },
            "empty-name" => valid with { UserName = "" },
            "long-name" => valid with { UserName = new string('u', 129) },
            "empty-password" => valid with { Password = "" },
            "short-password" => valid with { Password = "1234567" },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var result = new CreateUserCommandValidator().Validate(command);
        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == field);
    }

    [Fact]
    public void Create_accepts_boundary_lengths_and_empty_initial_access()
    {
        var command = ValidCreate() with { UserName = new string('u', 128), Password = "12345678" };
        Assert.True(new CreateUserCommandValidator().Validate(command).IsValid);
    }

    [Theory]
    [InlineData("null-roles")]
    [InlineData("null-scopes")]
    [InlineData("null-overrides")]
    [InlineData("null-override-item")]
    [InlineData("empty-permission")]
    [InlineData("invalid-type")]
    [InlineData("duplicate-permission")]
    [InlineData("global-with-scopes")]
    public void Access_rejects_invalid_collections_and_conflicting_scope(string scenario)
    {
        var valid = ValidAccess();
        var permission = Guid.NewGuid();
        var command = scenario switch
        {
            "null-roles" => valid with { RoleIds = null! },
            "null-scopes" => valid with { WarehouseIds = null! },
            "null-overrides" => valid with { Overrides = null! },
            "null-override-item" => valid with { Overrides = [null!] },
            "empty-permission" => valid with { Overrides = [new(Guid.Empty, OverrideType.Allow)] },
            "invalid-type" => valid with { Overrides = [new(permission, (OverrideType)99)] },
            "duplicate-permission" => valid with { Overrides = [new(permission, OverrideType.Allow), new(permission, OverrideType.Deny)] },
            "global-with-scopes" => valid with { HasAllWarehousesAccess = true, WarehouseIds = [Guid.NewGuid()] },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        Assert.False(new UpdateUserAccessValidator().Validate(command).IsValid);
    }

    [Fact]
    public void Access_accepts_removing_all_assignments_and_valid_override_types()
    {
        var validator = new UpdateUserAccessValidator();
        Assert.True(validator.Validate(ValidAccess()).IsValid);
        Assert.True(validator.Validate(ValidAccess() with { HasAllWarehousesAccess = true }).IsValid);
        Assert.True(validator.Validate(ValidAccess() with
        {
            RoleIds = [Guid.NewGuid()],
            WarehouseIds = [Guid.NewGuid()],
            Overrides = [new(Guid.NewGuid(), OverrideType.Allow), new(Guid.NewGuid(), OverrideType.Deny)]
        }).IsValid);
    }
}
