using System.Net.Http.Json;
using System.Net;
using System.Security.Claims;
using Inventory.API.Authentication;
using Inventory.Domain.Audit;
using Inventory.Domain.Identity;
using Inventory.Persistence;
using Inventory.Persistence.Seed;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;
using Shared.Contracts.Audit;
using Shared.Contracts.Identity;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class AuditQueryTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    private readonly OperationalApiData _data = new(fixture.Factory);

    [Fact]
    public async Task test_audit_filters_user_entity_action_and_inclusive_dates()
    {
        var reader = await _data.UserAsync([Permissions.Audit.Read], []);
        using var client = await _data.LoginAsync(reader);
        var time = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var entity = "Filtered-" + Guid.NewGuid().ToString("N");
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AuditLogs.AddRange(
                Log(reader.Id, time, entity, "Created"),
                Log(reader.Id, time.AddHours(1), entity, "Updated"),
                Log(null, time.AddHours(1), entity, "Updated"),
                Log(reader.Id, time.AddHours(2), entity, "Deleted"));
            await db.SaveChangesAsync();
        }
        var timestamp = Uri.EscapeDataString(time.AddHours(1).ToString("O"));
        var path = $"/api/v1/audit-logs?userId={reader.Id}&entity={entity}&action=Updated&fromUtc={timestamp}&toUtc={timestamp}";
        var page = await OperationalApiData.PageAsync<AuditLogDto>(client, path);
        var row = Assert.Single(page.Items);
        Assert.Equal(reader.Id, row.UserId);
        Assert.Equal("Updated", row.Action);
        Assert.Equal(1, page.TotalCount);
        Assert.Equal("value", row.NewValues!.Value.GetProperty("Name").GetString());
        var empty = await OperationalApiData.PageAsync<AuditLogDto>(client, path + "&page=2");
        Assert.Empty(empty.Items);
        Assert.Equal(1, empty.TotalCount);
    }

    [Fact]
    public async Task test_audit_equal_timestamps_have_stable_pages_without_duplicates()
    {
        var reader = await _data.UserAsync([Permissions.Audit.Read], []);
        using var client = await _data.LoginAsync(reader);
        var entity = "Stable-" + Guid.NewGuid().ToString("N");
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var time = DateTime.UtcNow;
            db.AuditLogs.AddRange(Enumerable.Range(0, 5).Select(_ => Log(reader.Id, time, entity, "Created")));
            await db.SaveChangesAsync();
        }
        var path = $"/api/v1/audit-logs?entity={entity}";
        var all = await OperationalApiData.PageAsync<AuditLogDto>(client, path);
        var ids = new List<Guid>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await OperationalApiData.PageAsync<AuditLogDto>(client, path + $"&pageSize=2&page={page}");
            Assert.Equal(5, result.TotalCount);
            ids.AddRange(result.Items.Select(r => r.Id));
        }
        Assert.Equal(all.Items.Select(r => r.Id), ids);
        Assert.Equal(5, ids.Distinct().Count());
        Assert.Equal(ids.Select(id => id.ToString()).OrderDescending(StringComparer.Ordinal), ids.Select(id => id.ToString()));
    }

    [Fact]
    public async Task test_user_creation_audits_role_and_scope_with_the_real_http_actor()
    {
        var data = await _data.SeedAsync();
        using var admin = await _data.LoginAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Name == RoleDefinitions.Viewer);
        var actor = await db.Users.SingleAsync(u => u.Email == DatabaseSeeder.DefaultAdminEmail);
        var correlation = Guid.NewGuid().ToString();
        admin.DefaultRequestHeaders.Add("X-Correlation-Id", correlation);
        using var response = await admin.PostAsJsonAsync("/api/v1/users", new
        {
            email = $"created-{Guid.NewGuid():N}@inventory.local",
            userName = "created",
            fullName = "Created",
            password = OperationalApiData.UserPassword,
            roleIds = new[] { role.Id },
            hasAllWarehousesAccess = false,
            warehouseScopeIds = new[] { data.Source }
        });
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var user = (await response.Content.ReadFromJsonAsync<UserDto>())!;
        var page = await OperationalApiData.PageAsync<AuditLogDto>(admin, $"/api/v1/audit-logs?userId={actor.Id}&action=Created&pageSize=200");
        var logs = page.Items.Where(l => l.CorrelationId == correlation).ToList();
        Assert.Equal(new[] { "User", "UserRole", "WarehouseScope" }, logs.Select(l => l.Entity).Order());
        Assert.All(logs, l => Assert.Equal(actor.Id, l.UserId));
        var assignment = logs.Single(l => l.Entity == "UserRole").NewValues!.Value;
        Assert.Equal(user.Id, assignment.GetProperty("UserId").GetGuid());
        Assert.Equal(role.Id, assignment.GetProperty("RoleId").GetGuid());
        Assert.All(logs, l => Assert.DoesNotMatch("(?i)password|hash|token|secret", l.NewValues!.Value.GetRawText()));
    }

    [Theory]
    [InlineData("Role")]
    [InlineData("Permission")]
    [InlineData("RolePermission")]
    [InlineData("UserRole")]
    [InlineData("UserPermissionOverride")]
    [InlineData("WarehouseScope")]
    public async Task test_authorization_entities_audit_create_change_and_delete(string entityName)
    {
        var data = await _data.SeedAsync();
        var actor = await _data.UserAsync([Permissions.Audit.Read], []);
        var correlation = Guid.NewGuid().ToString();
        Guid id;
        using (var scope = fixture.Factory.Services.CreateScope())
        using (new AuditActorScope(fixture.Factory.Services, actor.Id, correlation))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var role = new Role("Role-" + Guid.NewGuid().ToString("N"), "Initial");
            var permission = new Permission("test." + Guid.NewGuid().ToString("N"), "Initial");
            db.AddRange(role, permission);
            await db.SaveChangesAsync();
            var entity = AuthorizationEntity(entityName, role, permission, actor.Id, data.Source);
            if (entity != role && entity != permission) db.Add(entity);
            await db.SaveChangesAsync();
            id = (Guid)db.Entry(entity).Property("Id").CurrentValue!;
            UpdateMetadata(db, entity);
            await db.SaveChangesAsync();
            db.Remove(entity);
            db.SaveChanges();
        }
        using var client = await _data.LoginAsync(actor);
        var page = await OperationalApiData.PageAsync<AuditLogDto>(client, $"/api/v1/audit-logs?userId={actor.Id}&entity={entityName}&pageSize=200");
        var logs = page.Items.Where(l => (l.NewValues ?? l.OldValues)!.Value.GetProperty("Id").GetGuid() == id).ToList();
        var expected = entityName is "Role" or "Permission" or "UserPermissionOverride" ? 3 : 2;
        Assert.Equal(expected, logs.Count);
        Assert.Contains(logs, l => l.Action == "Created" && l.OldValues is null);
        Assert.Contains(logs, l => l.Action == "Deleted" && l.NewValues is null);
        Assert.All(logs, l => Assert.Equal(correlation, l.CorrelationId));
        if (expected == 3)
        {
            var changed = logs.Single(l => l.Action == "Updated");
            Assert.NotEqual(changed.OldValues!.Value.GetRawText(), changed.NewValues!.Value.GetRawText());
        }
    }

    [Fact]
    public async Task test_access_replacement_endpoints_audit_assignments_updates_and_removals()
    {
        var data = await _data.SeedAsync();
        var subject = await _data.UserAsync([], []);
        using var admin = await _data.LoginAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var role = await db.Roles.SingleAsync(r => r.Name == RoleDefinitions.Viewer);
        var permission = await db.Permissions.SingleAsync(p => p.Code == Permissions.Inventory.Read);
        var url = $"/api/v1/users/{subject.Id}/access";
        var access = new
        {
            roleIds = new[] { role.Id },
            overrides = new[] { new { permissionId = permission.Id, type = 1 } },
            hasAllWarehousesAccess = false,
            warehouseIds = new[] { data.Source }
        };
        var created = await AccessAuditAsync(admin, url, access);
        Assert.Equal(new[] { "UserPermissionOverride", "UserRole", "WarehouseScope" }, created.Select(l => l.Entity).Order());
        Assert.All(created, l => Assert.Equal("Created", l.Action));
        var updated = await AccessAuditAsync(admin, url, access with { overrides = [new { permissionId = permission.Id, type = 2 }] });
        var change = Assert.Single(updated);
        Assert.Equal("Updated", change.Action);
        Assert.Equal("UserPermissionOverride", change.Entity);
        Assert.Equal(1, change.OldValues!.Value.GetProperty("OverrideType").GetInt32());
        Assert.Equal(2, change.NewValues!.Value.GetProperty("OverrideType").GetInt32());
        Assert.Equal(change.OldValues.Value.GetProperty("Id").GetGuid(), change.NewValues.Value.GetProperty("Id").GetGuid());
        var deleted = await AccessAuditAsync(admin, url,
            new { roleIds = Array.Empty<Guid>(), overrides = Array.Empty<object>(), hasAllWarehousesAccess = false, warehouseIds = Array.Empty<Guid>() });
        Assert.Equal(3, deleted.Count);
        Assert.All(deleted, l =>
        {
            Assert.Equal("Deleted", l.Action);
            Assert.Equal(subject.Id, l.OldValues!.Value.GetProperty("UserId").GetGuid());
        });
    }

    private static async Task<IReadOnlyList<AuditLogDto>> AccessAuditAsync(HttpClient admin, string url, object body)
    {
        var correlation = Guid.NewGuid().ToString();
        admin.DefaultRequestHeaders.Remove("X-Correlation-Id");
        admin.DefaultRequestHeaders.Add("X-Correlation-Id", correlation);
        using var response = await admin.PutAsJsonAsync(url, body);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var page = await OperationalApiData.PageAsync<AuditLogDto>(admin, "/api/v1/audit-logs?pageSize=200");
        return page.Items.Where(l => l.CorrelationId == correlation).ToList();
    }

    [Fact]
    public async Task test_global_warehouse_access_audits_the_flag_and_removed_scopes()
    {
        var data = await _data.SeedAsync();
        var actor = await _data.UserAsync([Permissions.Audit.Read], []);
        var subject = await _data.UserAsync([Permissions.Inventory.Read], [data.Source]);
        var correlation = Guid.NewGuid().ToString();
        using (var scope = fixture.Factory.Services.CreateScope())
        using (new AuditActorScope(fixture.Factory.Services, actor.Id, correlation))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.Include(u => u.Scopes).SingleAsync(u => u.Id == subject.Id);
            user.GrantAllWarehousesAccess();
            await db.SaveChangesAsync();
        }
        using var client = await _data.LoginAsync(actor);
        var page = await OperationalApiData.PageAsync<AuditLogDto>(client, $"/api/v1/audit-logs?userId={actor.Id}&pageSize=200");
        var logs = page.Items.Where(l => l.CorrelationId == correlation).ToList();
        var change = logs.Single(l => l.Entity == "User");
        Assert.False(change.OldValues!.Value.GetProperty("HasAllWarehousesAccess").GetBoolean());
        Assert.True(change.NewValues!.Value.GetProperty("HasAllWarehousesAccess").GetBoolean());
        var removal = logs.Single(l => l.Entity == "WarehouseScope");
        Assert.Equal("Deleted", removal.Action);
        Assert.Equal(subject.Id, removal.OldValues!.Value.GetProperty("UserId").GetGuid());
        Assert.Equal(data.Source, removal.OldValues.Value.GetProperty("WarehouseId").GetGuid());
    }

    [Fact]
    public async Task test_rolled_back_authorization_change_has_no_persisted_audit()
    {
        var actor = await _data.UserAsync([Permissions.Audit.Read], []);
        var correlation = Guid.NewGuid().ToString();
        var name = "Rollback-" + Guid.NewGuid().ToString("N");
        using (var scope = fixture.Factory.Services.CreateScope())
        using (new AuditActorScope(fixture.Factory.Services, actor.Id, correlation))
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await using var transaction = await db.Database.BeginTransactionAsync();
            db.Roles.Add(new Role(name, ""));
            await db.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        using var readScope = fixture.Factory.Services.CreateScope();
        var reader = readScope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await reader.Roles.AnyAsync(r => r.Name == name));
        Assert.False(await reader.AuditLogs.AnyAsync(l => l.CorrelationId == correlation));
    }

    private static AuditLog Log(Guid? user, DateTime time, string entity, string action)
        => new(user, time, entity, action, null, "{\"Name\":\"value\"}", "127.0.0.1", Guid.NewGuid().ToString());

    private static object AuthorizationEntity(string name, Role role, Permission permission, Guid user, Guid warehouse)
        => name switch
        {
            "Role" => role,
            "Permission" => permission,
            "RolePermission" => new RolePermission(role.Id, permission.Id),
            "UserRole" => new UserRole(user, role.Id),
            "UserPermissionOverride" => new UserPermissionOverride(user, permission.Id, OverrideType.Allow),
            "WarehouseScope" => new WarehouseScope(user, warehouse),
            _ => throw new ArgumentOutOfRangeException(nameof(name))
        };

    private static void UpdateMetadata(AppDbContext db, object entity)
    {
        if (entity is Role or Permission) db.Entry(entity).Property("Description").CurrentValue = "Changed";
        if (entity is UserPermissionOverride) db.Entry(entity).Property("OverrideType").CurrentValue = OverrideType.Deny;
    }

    private sealed class AuditActorScope : IDisposable
    {
        private readonly IHttpContextAccessor _accessor;
        private readonly HttpContext? _previous;

        public AuditActorScope(IServiceProvider services, Guid user, string correlation)
        {
            _accessor = services.GetRequiredService<IHttpContextAccessor>();
            _previous = _accessor.HttpContext;
            _accessor.HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.ToString())], "test"))
            };
            _accessor.HttpContext.Items[CurrentUser.CorrelationIdItem] = correlation;
        }

        public void Dispose() => _accessor.HttpContext = _previous;
    }
}
