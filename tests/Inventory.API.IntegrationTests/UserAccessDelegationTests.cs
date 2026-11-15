using System.Net;
using System.Net.Http.Json;
using Inventory.Application.Features.Users;
using Inventory.Domain.Identity;
using Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;
using Shared.Contracts.Identity;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class UserAccessDelegationTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    private static readonly string[] Manage = [Permissions.Users.Manage, Permissions.Roles.Manage];
    private OperationalApiData Data => new(fixture.Factory);

    private async Task<(Guid Admin, Guid Viewer, Guid Read, Guid Create)> ReferencesAsync()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return ((await db.Roles.SingleAsync(r => r.Name == RoleDefinitions.Administrator)).Id,
            (await db.Roles.SingleAsync(r => r.Name == RoleDefinitions.Viewer)).Id,
            (await db.Permissions.SingleAsync(p => p.Code == Permissions.Products.Read)).Id,
            (await db.Permissions.SingleAsync(p => p.Code == Permissions.Products.Create)).Id);
    }

    private static object Access(Guid[] roles, object[] overrides, bool global = false, Guid[]? warehouses = null)
        => new { roleIds = roles, overrides, hasAllWarehousesAccess = global, warehouseIds = warehouses ?? [] };

    private static object Override(Guid id, OverrideType type) => new { permissionId = id, type };

    private static object Create(string email, Guid[] roles, bool global = false, Guid[]? warehouses = null)
        => new
        {
            email,
            userName = "delegate",
            fullName = "Delegate",
            password = OperationalApiData.UserPassword,
            roleIds = roles,
            hasAllWarehousesAccess = global,
            warehouseScopeIds = warehouses ?? []
        };

    [Fact]
    public async Task Own_access_change_is_forbidden_even_for_full_administrator()
    {
        var data = Data;
        var admin = await data.UserAsync([], [], true, RoleDefinitions.Administrator);
        using var client = await data.LoginAsync(admin);
        var refs = await ReferencesAsync();
        using var response = await client.PutAsJsonAsync($"/api/v1/users/{admin.Id}/access",
            Access([], []));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().Include(u => u.Roles).SingleAsync(u => u.Id == admin.Id);
        Assert.Contains(user.Roles, role => role.RoleId == refs.Admin);
        Assert.True(user.HasAllWarehousesAccess);
    }

    [Fact]
    public async Task Own_access_cannot_be_reassigned_or_have_a_deny_removed()
    {
        var data = Data;
        var refs = await ReferencesAsync();
        var caller = await data.UserAsync([.. Manage, Permissions.Products.Read], [], true,
            denies: [Permissions.Products.Read]);
        using var client = await data.LoginAsync(caller);
        var path = $"/api/v1/users/{caller.Id}/access";
        foreach (var overrides in new[]
        {
            new[] { Override(refs.Read, OverrideType.Deny) },
            Array.Empty<object>()
        })
        {
            using var response = await client.PutAsJsonAsync(path, Access([], overrides, true));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().Include(u => u.Overrides).SingleAsync(u => u.Id == caller.Id);
        Assert.Contains(user.Overrides, o => o.PermissionId == refs.Read && o.OverrideType == OverrideType.Deny);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Excessive_role_grant_is_forbidden_on_create_and_update(bool create)
    {
        var data = Data;
        var caller = await data.UserAsync(Manage, [], true);
        var target = await data.UserAsync([], [], false);
        using var client = await data.LoginAsync(caller);
        var refs = await ReferencesAsync();
        var email = $"delegate-{Guid.NewGuid():N}@test.local";
        using var response = create
            ? await client.PostAsJsonAsync("/api/v1/users", Create(email, [refs.Admin]))
            : await client.PutAsJsonAsync($"/api/v1/users/{target.Id}/access", Access([refs.Admin], []));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Email == email));
        Assert.Empty((await db.Users.Include(u => u.Roles).SingleAsync(u => u.Id == target.Id)).Roles);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Scoped_caller_cannot_grant_global_access_or_out_of_scope_warehouse(bool global)
    {
        var data = Data;
        var warehouses = await data.SeedAsync();
        var caller = await data.UserAsync(Manage, [warehouses.Source]);
        var target = await data.UserAsync([], [], false);
        using var client = await data.LoginAsync(caller);
        var email = $"delegate-{Guid.NewGuid():N}@test.local";
        using var create = await client.PostAsJsonAsync("/api/v1/users",
            Create(email, [], global,
                global ? [] : [warehouses.Source, warehouses.Destination]));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
        using var update = await client.PutAsJsonAsync($"/api/v1/users/{target.Id}/access",
            Access([], [], global, global ? [] : [warehouses.Source, warehouses.Destination]));
        Assert.Equal(HttpStatusCode.Forbidden, update.StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users.AnyAsync(u => u.Email == email));
        var unchanged = await db.Users.AsNoTracking().Include(u => u.Scopes)
            .SingleAsync(u => u.Id == target.Id);
        Assert.Empty(unchanged.Scopes);
        Assert.False(unchanged.HasAllWarehousesAccess);
    }

    [Fact]
    public async Task Caller_deny_prevents_delegating_permission_through_role_or_allow()
    {
        var data = Data;
        var caller = await data.UserAsync(Manage, [], true, RoleDefinitions.Viewer,
            [Permissions.Products.Read]);
        var target = await data.UserAsync([], [], false);
        using var client = await data.LoginAsync(caller);
        var refs = await ReferencesAsync();
        foreach (var access in new[]
        {
            Access([refs.Viewer], []),
            Access([], [Override(refs.Read, OverrideType.Allow)])
        })
        {
            using var response = await client.PutAsJsonAsync($"/api/v1/users/{target.Id}/access", access);
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Target_deny_does_not_hide_excessive_role_grant_and_rejection_preserves_access()
    {
        var data = Data;
        var warehouses = await data.SeedAsync();
        var refs = await ReferencesAsync();
        var caller = await data.UserAsync(Manage, [warehouses.Source]);
        var target = await data.UserAsync([Permissions.Products.Create], [warehouses.Source], role: RoleDefinitions.Viewer);
        Guid originalOverrideId;
        using (var originalScope = fixture.Factory.Services.CreateScope())
        {
            var originalDb = originalScope.ServiceProvider.GetRequiredService<AppDbContext>();
            originalOverrideId = await originalDb.UserPermissionOverrides
                .Where(o => o.UserId == target.Id && o.PermissionId == refs.Create)
                .Select(o => o.Id).SingleAsync();
        }
        using var client = await data.LoginAsync(caller);
        var path = $"/api/v1/users/{target.Id}/access";
        using var response = await client.PutAsJsonAsync(path,
            Access([refs.Viewer], [Override(refs.Read, OverrideType.Deny),
                Override(await PermissionIdAsync(Permissions.Inventory.Read), OverrideType.Deny),
                Override(await PermissionIdAsync(Permissions.Reports.Read), OverrideType.Deny)],
                false, [warehouses.Source]));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var unchanged = await db.Users.AsNoTracking().Include(u => u.Overrides).Include(u => u.Scopes)
            .Include(u => u.Roles).SingleAsync(u => u.Id == target.Id);
        Assert.Equal([refs.Viewer], unchanged.Roles.Select(r => r.RoleId).ToArray());
        Assert.Contains(unchanged.Overrides, o => o.Id == originalOverrideId &&
            o.PermissionId == refs.Create && o.OverrideType == OverrideType.Allow);
        Assert.DoesNotContain(unchanged.Overrides, o => o.PermissionId == refs.Read);
        Assert.Equal([warehouses.Source], unchanged.Scopes.Select(s => s.WarehouseId).ToArray());
        Assert.False(unchanged.HasAllWarehousesAccess);
    }

    [Fact]
    public async Task Within_ceiling_allows_scoped_create_and_override_update()
    {
        var data = Data;
        var warehouses = await data.SeedAsync();
        var refs = await ReferencesAsync();
        var caller = await data.UserAsync([.. Manage, Permissions.Products.Read], [warehouses.Source]);
        using var client = await data.LoginAsync(caller);
        using var allowed = await client.PostAsJsonAsync("/api/v1/users",
            Create($"delegate-{Guid.NewGuid():N}@test.local", [], false, [warehouses.Source]));
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var target = (await allowed.Content.ReadFromJsonAsync<UserDto>())!;
        using var updated = await client.PutAsJsonAsync($"/api/v1/users/{target.Id}/access",
            Access([], [Override(refs.Read, OverrideType.Allow), Override(refs.Create, OverrideType.Deny)],
                false, [warehouses.Source]));
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
    }

    [Fact]
    public async Task Within_ceiling_role_and_global_scope_can_be_created_and_updated()
    {
        var data = Data;
        var refs = await ReferencesAsync();
        var globalCaller = await data.UserAsync([.. Manage, Permissions.Products.Read,
            Permissions.Inventory.Read, Permissions.Reports.Read], [], true);
        using var globalClient = await data.LoginAsync(globalCaller);
        using var created = await globalClient.PostAsJsonAsync("/api/v1/users",
            Create($"delegate-{Guid.NewGuid():N}@test.local", [refs.Viewer], true));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var target = (await created.Content.ReadFromJsonAsync<UserDto>())!;
        using var roleUpdate = await globalClient.PutAsJsonAsync($"/api/v1/users/{target.Id}/access",
            Access([refs.Viewer], [Override(refs.Create, OverrideType.Deny)], true));
        Assert.Equal(HttpStatusCode.NoContent, roleUpdate.StatusCode);
        var warehouses = await data.SeedAsync();
        using var explicitUpdate = await globalClient.PutAsJsonAsync($"/api/v1/users/{target.Id}/access",
            Access([refs.Viewer], [Override(refs.Create, OverrideType.Deny)], false,
                [warehouses.Source, warehouses.Destination]));
        Assert.Equal(HttpStatusCode.NoContent, explicitUpdate.StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.AsNoTracking().Include(u => u.Scopes).SingleAsync(u => u.Id == target.Id);
        Assert.False(user.HasAllWarehousesAccess);
        Assert.Equal(new[] { warehouses.Source, warehouses.Destination }.Order(),
            user.Scopes.Select(s => s.WarehouseId).Order().ToArray());
    }

    private async Task<Guid> PermissionIdAsync(string code)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return (await db.Permissions.SingleAsync(p => p.Code == code)).Id;
    }
}
