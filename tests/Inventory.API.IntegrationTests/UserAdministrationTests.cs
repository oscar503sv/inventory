using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Inventory.Application.Features.Auth;
using Inventory.Application.Features.Users;
using Inventory.Persistence.Seed;
using Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Identity;
using Shared.Contracts.Warehouses;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class UserAdministrationTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    private const string Password = "Users#123456";

    private async Task<(HttpClient Client, AuthResultDto Auth)> Login(string? email = null)
    {
        var client = fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email = email ?? DatabaseSeeder.DefaultAdminEmail,
            password = email is null ? DatabaseSeeder.DefaultAdminPassword : Password
        });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResultDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    [Fact]
    public async Task Concurrent_refresh_issues_exactly_one_successor()
    {
        var (client, auth) = await Login();
        using var ownedClient = client;
        fixture.RotationGate.Arm();
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/v1/auth/refresh", new { auth.RefreshToken }),
            client.PostAsJsonAsync("/api/v1/auth/refresh", new { auth.RefreshToken }));
        try
        {
            var success = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Forbidden);
            var rotated = (await success.Content.ReadFromJsonAsync<AuthResultDto>())!;
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tokens = scope.ServiceProvider.GetRequiredService<Inventory.Application.Common.Abstractions.IRefreshTokenService>();
            var original = await db.RefreshTokens.SingleAsync(t => t.TokenHash == tokens.Hash(auth.RefreshToken));
            Assert.NotNull(original.RevokedAtUtc);
            Assert.Equal(tokens.Hash(rotated.RefreshToken), original.ReplacedByTokenHash);
            Assert.Single(await db.RefreshTokens.Where(t => t.TokenHash == original.ReplacedByTokenHash
                && t.RevokedAtUtc == null).ToListAsync());
            using var next = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { rotated.RefreshToken });
            Assert.Equal(HttpStatusCode.OK, next.StatusCode);
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }
    }

    private static async Task<UserDto> Create(HttpClient admin)
    {
        var response = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            email = $"{Guid.NewGuid():N}@test.local",
            userName = "user",
            fullName = "Test User",
            password = Password,
            roleIds = Array.Empty<Guid>(),
            hasAllWarehousesAccess = false,
            warehouseScopeIds = Array.Empty<Guid>()
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>())!;
    }

    [Fact]
    public async Task Access_replacement_applies_roles_overrides_and_scopes_to_existing_tokens()
    {
        var (admin, _) = await Login();
        using var adminLifetime = admin;
        var user = await Create(admin);
        var (client, _) = await Login(user.Email);
        using var clientLifetime = client;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/products")).StatusCode);
        var roles = (await admin.GetFromJsonAsync<RoleDto[]>("/api/v1/roles"))!;
        var permissions = (await admin.GetFromJsonAsync<PermissionCatalogItem[]>("/api/v1/permissions"))!;
        var read = permissions.Single(p => p.Code == "products.read").Id;
        var role = roles.Single(r => r.Name == "Viewer").Id;
        var access = new { roleIds = new[] { role }, overrides = Array.Empty<object>(), hasAllWarehousesAccess = true, warehouseIds = Array.Empty<Guid>() };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/v1/users/{user.Id}/access", access)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/products")).StatusCode);
        await SetOverride(admin, user.Id, role, read, 2);
        Assert.DoesNotContain("products.read", (await client.GetFromJsonAsync<CurrentUserDto>("/api/v1/users/me"))!.Permissions);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/v1/products")).StatusCode);
        await SetOverride(admin, user.Id, role, read, 1);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/products")).StatusCode);
        var empty = new { roleIds = Array.Empty<Guid>(), overrides = Array.Empty<object>(), hasAllWarehousesAccess = false, warehouseIds = Array.Empty<Guid>() };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/v1/users/{user.Id}/access", empty)).StatusCode);
        var me = (await client.GetFromJsonAsync<CurrentUserDto>("/api/v1/users/me"))!;
        Assert.Empty(me.Permissions);
        Assert.False(me.HasAllWarehousesAccess);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/v1/users/{user.Id}/access", access)).StatusCode);
    }

    private static async Task SetOverride(HttpClient admin, Guid user, Guid role, Guid permission, int type)
    {
        var response = await admin.PutAsJsonAsync($"/api/v1/users/{user}/access", new
        {
            roleIds = new[] { role },
            overrides = new[] { new { permissionId = permission, type } },
            hasAllWarehousesAccess = true,
            warehouseIds = Array.Empty<Guid>()
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Rotation_logout_and_admin_revocation_prevent_refresh_but_keep_access_until_expiry()
    {
        var (admin, _) = await Login();
        using var adminLifetime = admin;
        var user = await Create(admin);
        var (client, auth) = await Login(user.Email);
        using var lifetime = client;
        var rotatedResponse = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { auth.RefreshToken });
        rotatedResponse.EnsureSuccessStatusCode();
        var rotated = (await rotatedResponse.Content.ReadFromJsonAsync<AuthResultDto>())!;
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { auth.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { rotated.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/users/me")).StatusCode);
        var (other, session) = await Login(user.Email);
        using var otherLifetime = other;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/users/{user.Id}/sessions")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await other.PostAsJsonAsync("/api/v1/auth/refresh", new { session.RefreshToken })).StatusCode);
    }

    [Fact]
    public async Task Deactivation_blocks_access_and_login_and_activation_does_not_restore_refresh()
    {
        var (admin, _) = await Login();
        using var adminLifetime = admin;
        var user = await Create(admin);
        var (client, auth) = await Login(user.Email);
        using var lifetime = client;
        var path = $"/api/v1/users/{user.Id}";
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(path, new { user.Email, user.UserName, fullName = "Edited", isActive = false })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/users/me")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/auth/login", new { user.Email, password = Password })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(path, new { user.Email, user.UserName, fullName = "Edited", isActive = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { auth.RefreshToken })).StatusCode);
        Assert.Equal("Edited", (await client.GetFromJsonAsync<CurrentUserDto>("/api/v1/users/me"))!.FullName);
    }

    [Fact]
    public async Task Warehouse_scopes_can_be_granted_and_removed_and_invalid_overrides_return_400()
    {
        var (admin, _) = await Login();
        using var lifetime = admin;
        var user = await Create(admin);
        var response = await admin.PostAsJsonAsync("/api/v1/warehouses", new
        {
            code = Guid.NewGuid().ToString("N")[..12],
            name = "Scope test",
            address = "A"
        });
        response.EnsureSuccessStatusCode();
        var warehouse = (await response.Content.ReadFromJsonAsync<WarehouseDto>())!;
        var path = $"/api/v1/users/{user.Id}/access";
        var access = new
        {
            roleIds = Array.Empty<Guid>(),
            overrides = Array.Empty<object>(),
            hasAllWarehousesAccess = false,
            warehouseIds = new[] { warehouse.Id }
        };
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(path, access)).StatusCode);
        Assert.Equal(new[] { warehouse.Id }, (await admin.GetFromJsonAsync<UpdateUserAccessCommand>(path))!.WarehouseIds);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(path, access with { warehouseIds = Array.Empty<Guid>() })).StatusCode);
        Assert.Empty((await admin.GetFromJsonAsync<UpdateUserAccessCommand>(path))!.WarehouseIds);
        var invalid = new
        {
            roleIds = Array.Empty<Guid>(),
            overrides = new[] { new { permissionId = Guid.NewGuid(), type = 99 } },
            hasAllWarehousesAccess = false,
            warehouseIds = Array.Empty<Guid>()
        };
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(path, invalid)).StatusCode);
    }

    [Fact]
    public async Task Profile_update_rejects_invalid_email_duplicates_and_missing_users()
    {
        var (admin, _) = await Login();
        using var lifetime = admin;
        var user = await Create(admin);
        var body = new { email = "invalid", userName = "user", fullName = "Test", isActive = true };
        var path = $"/api/v1/users/{user.Id}";
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(path, body)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync(path, body with { email = DatabaseSeeder.DefaultAdminEmail })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PutAsJsonAsync($"/api/v1/users/{Guid.NewGuid()}", body with { email = user.Email })).StatusCode);
    }

    [Fact]
    public async Task Expired_refresh_and_anonymous_logout_are_rejected()
    {
        using var anonymous = fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/v1/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/v1/users/me")).StatusCode);
        var (admin, _) = await Login();
        using var adminLifetime = admin;
        var user = await Create(admin);
        var (client, auth) = await Login(user.Email);
        using var lifetime = client;
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.RefreshTokens.Where(t => t.UserId == user.Id)
            .ExecuteUpdateAsync(setters => setters.SetProperty(t => t.ExpiresAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/auth/refresh", new { auth.RefreshToken })).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Creation_rejects_unknown_warehouse_even_with_global_access(bool global)
    {
        var (admin, _) = await Login();
        using var lifetime = admin;
        var response = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            email = $"{Guid.NewGuid():N}@test.local",
            userName = "invalid-scope",
            fullName = "Test",
            password = Password,
            roleIds = Array.Empty<Guid>(),
            hasAllWarehousesAccess = global,
            warehouseScopeIds = new[] { Guid.NewGuid() }
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData("roles")]
    [InlineData("permissions")]
    [InlineData("warehouses")]
    public async Task Unknown_access_references_are_rejected_without_partial_changes(string kind)
    {
        var (admin, _) = await Login();
        using var lifetime = admin;
        var user = await Create(admin);
        var response = await admin.PutAsJsonAsync($"/api/v1/users/{user.Id}/access", new
        {
            roleIds = kind == "roles" ? new[] { Guid.NewGuid() } : Array.Empty<Guid>(),
            overrides = kind == "permissions" ? new[] { new { permissionId = Guid.NewGuid(), type = 1 } } : [],
            hasAllWarehousesAccess = false,
            warehouseIds = kind == "warehouses" ? new[] { Guid.NewGuid() } : Array.Empty<Guid>()
        });
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var access = (await admin.GetFromJsonAsync<UpdateUserAccessCommand>($"/api/v1/users/{user.Id}/access"))!;
        Assert.Empty(access.RoleIds);
        Assert.Empty(access.Overrides);
        Assert.Empty(access.WarehouseIds);
    }
}
