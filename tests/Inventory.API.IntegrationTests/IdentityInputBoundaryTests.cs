using System.Net;
using System.Net.Http.Json;
using Inventory.Application.Features.Users;
using Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Identity;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class IdentityInputBoundaryTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    private static CreateUserCommand Create(string email) => new(email, "user", "User", "Password#123", [], false, []);

    [Theory]
    [InlineData("email")]
    [InlineData("name")]
    [InlineData("password-ascii")]
    [InlineData("password-utf8")]
    [InlineData("scope")]
    public async Task Invalid_creation_is_a_400_without_persisting_user_or_audit(string scenario)
    {
        using var client = await new OperationalApiData(fixture.Factory).LoginAsync();
        var valid = Create($"{Guid.NewGuid():N}@boundary.test");
        var command = scenario switch
        {
            "email" => valid with { Email = new string('e', 246) + "@test.local" },
            "name" => valid with { FullName = new string('n', 257) },
            "password-ascii" => valid with { Password = new string('p', 73) },
            "password-utf8" => valid with { Password = new string('é', 37) },
            "scope" => valid with { HasAllWarehousesAccess = true, WarehouseScopeIds = [Guid.NewGuid()] },
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var count = await db.Users.CountAsync();
        var audit = await db.AuditLogs.CountAsync();
        await OperationalApiData.ProblemAsync(await client.PostAsJsonAsync("/api/v1/users", command), HttpStatusCode.BadRequest);
        Assert.Equal(count, await db.Users.CountAsync());
        Assert.Equal(audit, await db.AuditLogs.CountAsync());
    }

    [Theory]
    [InlineData("ascii")]
    [InlineData("utf8")]
    public async Task Exact_storage_and_password_byte_limits_allow_creation_and_login(string encoding)
    {
        using var admin = await new OperationalApiData(fixture.Factory).LoginAsync();
        var email = new string('e', 213) + Guid.NewGuid().ToString("N") + "@test.local";
        Assert.Equal(256, email.Length);
        var password = encoding == "ascii" ? new string('p', 72) : new string('é', 36);
        var command = Create(email) with { Password = password, UserName = new string('u', 128), FullName = new string('n', 256) };
        using var response = await admin.PostAsJsonAsync("/api/v1/users", command);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = (await response.Content.ReadFromJsonAsync<UserDto>())!;
        using var anonymous = fixture.Factory.CreateClient();
        using var login = await anonymous.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var changed = await admin.PutAsJsonAsync($"/api/v1/users/{user.Id}", new
        { email, command.UserName, fullName = new string('n', 257), isActive = true });
        Assert.Equal(HttpStatusCode.BadRequest, changed.StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(256, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id)).FullName.Length);
    }

    [Theory]
    [InlineData("ascii")]
    [InlineData("utf8")]
    public async Task Oversized_login_password_is_a_validation_error(string encoding)
    {
        using var client = fixture.Factory.CreateClient();
        var password = encoding == "ascii" ? new string('p', 73) : new string('é', 37);
        await OperationalApiData.ProblemAsync(await client.PostAsJsonAsync("/api/v1/auth/login", new
        { email = "unknown@boundary.test", password }), HttpStatusCode.BadRequest);
    }
}
