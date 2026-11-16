using Inventory.Application.Features.Auth;
using Inventory.Application.Features.Users;

namespace Inventory.Application.Tests.Validators;

public sealed class IdentityBoundaryValidatorTests
{
    private static CreateUserCommand Create() => new("user@test.local", "user", "User", "Password#123", [], false, []);

    [Theory]
    [InlineData("email")]
    [InlineData("name")]
    [InlineData("username")]
    [InlineData("null-name")]
    public void Profile_rules_are_shared_for_creation_and_update(string field)
    {
        var valid = Create();
        var command = field switch
        {
            "email" => valid with { Email = new string('e', 246) + "@test.local" },
            "name" => valid with { FullName = new string('n', 257) },
            "username" => valid with { UserName = new string('u', 129) },
            "null-name" => valid with { FullName = null! },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
        Assert.False(new CreateUserCommandValidator().Validate(command).IsValid);
        Assert.False(new UpdateUserValidator().Validate(new UpdateUserCommand(Guid.NewGuid(), command.Email, command.UserName, command.FullName, true)).IsValid);
    }

    [Fact]
    public void Profile_accepts_exact_storage_boundaries()
    {
        var command = Create() with
        {
            Email = new string('e', 245) + "@test.local",
            UserName = new string('u', 128),
            FullName = new string('n', 256)
        };
        Assert.Equal(256, command.Email.Length);
        Assert.True(new CreateUserCommandValidator().Validate(command).IsValid);
        Assert.True(new UpdateUserValidator().Validate(new UpdateUserCommand(Guid.NewGuid(), command.Email, command.UserName, command.FullName, true)).IsValid);
    }

    [Theory]
    [InlineData("ascii", 72, true)]
    [InlineData("ascii", 73, false)]
    [InlineData("ascii", 129, false)]
    [InlineData("accent", 36, true)]
    [InlineData("accent", 37, false)]
    [InlineData("emoji", 18, true)]
    [InlineData("emoji", 19, false)]
    public void Password_limits_use_utf8_bytes_in_creation_and_login(string kind, int count, bool valid)
    {
        var part = kind switch { "ascii" => "a", "accent" => "é", "emoji" => "😀", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
        var password = string.Concat(Enumerable.Repeat(part, count));
        Assert.Equal(valid, new CreateUserCommandValidator().Validate(Create() with { Password = password }).IsValid);
        Assert.Equal(valid, new LoginCommandValidator().Validate(new LoginCommand("user@test.local", password)).IsValid);
    }

    [Fact]
    public void Short_password_attempts_can_be_checked_but_cannot_create_new_accounts()
    {
        Assert.True(new LoginCommandValidator().Validate(new LoginCommand("user@test.local", "short")).IsValid);
        Assert.False(new CreateUserCommandValidator().Validate(Create() with { Password = "short" }).IsValid);
    }

    [Fact]
    public void Scope_rules_reject_global_with_individual_warehouses_and_null_collections()
    {
        var id = Guid.NewGuid();
        var create = new CreateUserCommandValidator();
        var update = new UpdateUserAccessValidator();
        Assert.False(create.Validate(Create() with { HasAllWarehousesAccess = true, WarehouseScopeIds = [id] }).IsValid);
        Assert.False(update.Validate(new UpdateUserAccessCommand(id, [], [], true, [id])).IsValid);
        Assert.False(create.Validate(Create() with { WarehouseScopeIds = null! }).IsValid);
        Assert.False(update.Validate(new UpdateUserAccessCommand(id, [], [], false, null!)).IsValid);
        Assert.True(create.Validate(Create() with { HasAllWarehousesAccess = true }).IsValid);
        Assert.True(update.Validate(new UpdateUserAccessCommand(id, [], [], true, [])).IsValid);
    }
}
