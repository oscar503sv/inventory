using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Inventory.Application.Features.Users;
using Inventory.Application.Common.Abstractions;
using Inventory.Domain.Common;
using Inventory.Domain.Identity;
using Inventory.Domain.Identity.Events;
using Inventory.Persistence;
using Inventory.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shared.Authorization;
using Shared.Contracts.Identity;
using Shared.Contracts.Warehouses;

namespace Inventory.API.IntegrationTests;

public sealed class AdministratorProtectionTests
{
    private const string Password = "Users#123456";
    private const long AdministratorUpdateLockKey = 471047;

    [Fact]
    public async Task Administrator_transaction_dispatches_events_after_commit_and_discards_rolled_back_events()
    {
        var fixture = new PostgresApiFixture();
        await fixture.InitializeAsync();
        try
        {
            using var scope = fixture.Factory.Services.CreateScope();
            var connectionString = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString()!;
            var dispatcher = new ObservingDispatcher(connectionString);
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
            var rolledBack = User.Create($"{Guid.NewGuid():N}@test.local", "rollback", "Rollback", "hash");
            await using (var db = new AppDbContext(options, dispatcher))
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() => db.ExecuteAdministratorUpdateAsync(async () =>
                {
                    db.Users.Add(rolledBack);
                    await db.SaveChangesAsync();
                    Assert.Empty(dispatcher.Seen);
                    throw new InvalidOperationException("Abort transaction");
                }, CancellationToken.None));
            }
            Assert.Empty(dispatcher.Seen);
            await using (var db = new AppDbContext(options, dispatcher))
                Assert.False(await db.Users.AnyAsync(u => u.Id == rolledBack.Id));

            var committed = User.Create($"{Guid.NewGuid():N}@test.local", "committed", "Committed", "hash");
            await using (var db = new AppDbContext(options, dispatcher))
            {
                await db.ExecuteAdministratorUpdateAsync(async () =>
                {
                    db.Users.Add(committed);
                    await db.SaveChangesAsync();
                    Assert.Empty(dispatcher.Seen);
                }, CancellationToken.None);
            }
            Assert.Equal(new[] { committed.Id }, dispatcher.Seen);
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Fact]
    public async Task Administrator_cannot_deactivate_self_or_change_email_but_can_edit_name_with_normalized_email()
    {
        var fixture = new PostgresApiFixture();
        await fixture.InitializeAsync();
        try
        {
            using var admin = await LoginAsync(fixture, DatabaseSeeder.DefaultAdminEmail, DatabaseSeeder.DefaultAdminPassword);
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == DatabaseSeeder.DefaultAdminEmail);
            var path = $"/api/v1/users/{user.Id}";
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync(path,
                new { user.Email, user.UserName, user.FullName, isActive = false })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await admin.PutAsJsonAsync(path,
                new { email = "changed@test.local", user.UserName, user.FullName, isActive = true })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(path,
                new { email = $" {user.Email.ToUpperInvariant()} ", user.UserName, fullName = "Edited Administrator", isActive = true })).StatusCode);
            var persisted = await db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id);
            Assert.True(persisted.IsActive);
            Assert.Equal(user.Email, persisted.Email);
            Assert.Equal("Edited Administrator", persisted.FullName);
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Fact]
    public async Task Inactive_replacement_does_not_allow_removing_last_administrator_and_rejections_are_atomic()
    {
        var fixture = new PostgresApiFixture();
        await fixture.InitializeAsync();
        try
        {
            using var seed = await LoginAsync(fixture, DatabaseSeeder.DefaultAdminEmail, DatabaseSeeder.DefaultAdminPassword);
            var administrator = (await seed.GetFromJsonAsync<RoleDto[]>("/api/v1/roles"))!
                .Single(r => r.Name == RoleDefinitions.Administrator).Id;
            var inactive = await CreateAsync(seed);
            await SetAccessAsync(seed, inactive.Id, [administrator], []);
            Assert.Equal(HttpStatusCode.NoContent, (await seed.PutAsJsonAsync($"/api/v1/users/{inactive.Id}",
                new { inactive.Email, inactive.UserName, inactive.FullName, isActive = false })).StatusCode);
            var actor = await CreateAsync(seed);
            var permissions = (await seed.GetFromJsonAsync<PermissionCatalogItem[]>("/api/v1/permissions?limit=100"))!;
            await SetAccessAsync(seed, actor.Id, [], permissions.Select(p => p.Id).ToArray());
            using var caller = await LoginAsync(fixture, actor.Email, Password);
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var target = await db.Users.AsNoTracking().SingleAsync(u => u.Email == DatabaseSeeder.DefaultAdminEmail);
            var auditBefore = await db.AuditLogs.CountAsync();
            var tokensBefore = await db.RefreshTokens.CountAsync(t => t.UserId == target.Id && t.RevokedAtUtc != null);
            var profile = await caller.PutAsJsonAsync($"/api/v1/users/{target.Id}",
                new { target.Email, target.UserName, fullName = "Rejected", isActive = false });
            Assert.Equal(HttpStatusCode.Conflict, profile.StatusCode);
            var access = await caller.PutAsJsonAsync($"/api/v1/users/{target.Id}/access", new
            {
                roleIds = Array.Empty<Guid>(),
                overrides = Array.Empty<object>(),
                hasAllWarehousesAccess = true,
                warehouseIds = Array.Empty<Guid>()
            });
            Assert.Equal(HttpStatusCode.Conflict, access.StatusCode);
            Assert.Equal(auditBefore, await db.AuditLogs.CountAsync());
            Assert.Equal(tokensBefore, await db.RefreshTokens.CountAsync(t => t.UserId == target.Id && t.RevokedAtUtc != null));
            var persisted = await db.Users.AsNoTracking().SingleAsync(u => u.Id == target.Id);
            Assert.True(persisted.IsActive);
            Assert.Equal(target.FullName, persisted.FullName);
            Assert.True(await db.UserRoles.AnyAsync(r => r.UserId == target.Id && r.RoleId == administrator));
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Fact]
    public async Task Editing_administrator_requires_all_target_permissions_even_when_target_denies_one()
    {
        var fixture = new PostgresApiFixture();
        await fixture.InitializeAsync();
        try
        {
            using var seed = await LoginAsync(fixture, DatabaseSeeder.DefaultAdminEmail, DatabaseSeeder.DefaultAdminPassword);
            var actor = await CreateAsync(seed);
            var permissions = (await seed.GetFromJsonAsync<PermissionCatalogItem[]>("/api/v1/permissions?limit=100"))!;
            var manage = permissions.Where(p => p.Code is Permissions.Users.Manage or Permissions.Roles.Manage)
                .Select(p => p.Id).ToArray();
            await SetAccessAsync(seed, actor.Id, [], manage);
            using var caller = await LoginAsync(fixture, actor.Email, Password);
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var target = await db.Users.AsNoTracking().SingleAsync(u => u.Email == DatabaseSeeder.DefaultAdminEmail);
            var path = $"/api/v1/users/{target.Id}";
            Assert.Equal(HttpStatusCode.Forbidden, (await caller.PutAsJsonAsync(path,
                new { target.Email, target.UserName, fullName = "Blocked", isActive = true })).StatusCode);
            Assert.Equal(target.FullName, (await db.Users.AsNoTracking().SingleAsync(u => u.Id == target.Id)).FullName);

            var administrator = (await seed.GetFromJsonAsync<RoleDto[]>("/api/v1/roles"))!
                .Single(r => r.Name == RoleDefinitions.Administrator).Id;
            var denied = permissions.First(p => p.Code != Permissions.Users.Manage && p.Code != Permissions.Roles.Manage);
            Assert.Equal(HttpStatusCode.NoContent, (await seed.PutAsJsonAsync($"/api/v1/users/{target.Id}/access", new
            {
                roleIds = new[] { administrator },
                overrides = new[] { new { permissionId = denied.Id, type = 2 } },
                hasAllWarehousesAccess = true,
                warehouseIds = Array.Empty<Guid>()
            })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await caller.PutAsJsonAsync(path,
                new { target.Email, target.UserName, fullName = "Still blocked", isActive = true })).StatusCode);
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Fact]
    public async Task Actor_deny_and_warehouse_scope_limit_administrator_edits()
    {
        var fixture = new PostgresApiFixture();
        await fixture.InitializeAsync();
        try
        {
            using var seed = await LoginAsync(fixture, DatabaseSeeder.DefaultAdminEmail, DatabaseSeeder.DefaultAdminPassword);
            var actor = await CreateAsync(seed);
            var permissions = (await seed.GetFromJsonAsync<PermissionCatalogItem[]>("/api/v1/permissions?limit=100"))!;
            var allIds = permissions.Select(p => p.Id).ToArray();
            var denied = permissions.First(p => p.Code != Permissions.Users.Manage && p.Code != Permissions.Roles.Manage);
            var administrator = (await seed.GetFromJsonAsync<RoleDto[]>("/api/v1/roles"))!
                .Single(r => r.Name == RoleDefinitions.Administrator).Id;
            var target = await CreateAsync(seed);
            await SetAccessAsync(seed, target.Id, [administrator], []);
            await SetAccessAsync(seed, actor.Id, [], allIds);
            using var caller = await LoginAsync(fixture, actor.Email, Password);
            var profilePath = $"/api/v1/users/{target.Id}";
            var accessPath = $"/api/v1/users/{target.Id}/access";
            Assert.Equal(HttpStatusCode.NoContent, (await seed.PutAsJsonAsync($"/api/v1/users/{actor.Id}/access", new
            {
                roleIds = Array.Empty<Guid>(),
                overrides = allIds.Select(id => new { permissionId = id, type = id == denied.Id ? 2 : 1 }).ToArray(),
                hasAllWarehousesAccess = true,
                warehouseIds = Array.Empty<Guid>()
            })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await caller.PutAsJsonAsync(profilePath,
                new { target.Email, target.UserName, fullName = "Denied", isActive = true })).StatusCode);
            await SetAccessAsync(seed, actor.Id, [], allIds);

            var warehouseResponse = await seed.PostAsJsonAsync("/api/v1/warehouses", new
            {
                code = Guid.NewGuid().ToString("N")[..12],
                name = "Protected warehouse",
                address = "A"
            });
            warehouseResponse.EnsureSuccessStatusCode();
            var warehouse = (await warehouseResponse.Content.ReadFromJsonAsync<WarehouseDto>())!;
            var otherWarehouseResponse = await seed.PostAsJsonAsync("/api/v1/warehouses", new
            {
                code = Guid.NewGuid().ToString("N")[..12],
                name = "Other warehouse",
                address = "B"
            });
            otherWarehouseResponse.EnsureSuccessStatusCode();
            var otherWarehouse = (await otherWarehouseResponse.Content.ReadFromJsonAsync<WarehouseDto>())!;
            Assert.Equal(HttpStatusCode.NoContent, (await seed.PutAsJsonAsync($"/api/v1/users/{actor.Id}/access", new
            {
                roleIds = Array.Empty<Guid>(),
                overrides = allIds.Select(permissionId => new { permissionId, type = 1 }).ToArray(),
                hasAllWarehousesAccess = false,
                warehouseIds = new[] { warehouse.Id }
            })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await caller.PutAsJsonAsync(profilePath,
                new { target.Email, target.UserName, fullName = "Global blocked", isActive = true })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await seed.PutAsJsonAsync(accessPath, new
            {
                roleIds = new[] { administrator },
                overrides = Array.Empty<object>(),
                hasAllWarehousesAccess = false,
                warehouseIds = new[] { otherWarehouse.Id }
            })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await caller.PutAsJsonAsync(profilePath,
                new { target.Email, target.UserName, fullName = "Other scope blocked", isActive = true })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await seed.PutAsJsonAsync(accessPath, new
            {
                roleIds = new[] { administrator },
                overrides = Array.Empty<object>(),
                hasAllWarehousesAccess = false,
                warehouseIds = new[] { warehouse.Id }
            })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await caller.PutAsJsonAsync(accessPath, new
            {
                roleIds = new[] { administrator },
                overrides = Array.Empty<object>(),
                hasAllWarehousesAccess = true,
                warehouseIds = Array.Empty<Guid>()
            })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await caller.PutAsJsonAsync(profilePath,
                new { target.Email, target.UserName, fullName = "Scoped edit", isActive = true })).StatusCode);
        }
        finally { await fixture.DisposeAsync(); }
    }

    [Theory]
    [InlineData("deactivate", "deactivate")]
    [InlineData("remove-role", "remove-role")]
    [InlineData("deactivate", "remove-role")]
    public async Task Concurrent_changes_leave_one_active_administrator(string firstAction, string secondAction)
    {
        var fixture = new PostgresApiFixture();
        await fixture.InitializeAsync();
        try
        {
            using var seed = await LoginAsync(fixture, DatabaseSeeder.DefaultAdminEmail, DatabaseSeeder.DefaultAdminPassword);
            var roles = (await seed.GetFromJsonAsync<RoleDto[]>("/api/v1/roles"))!;
            var administrator = roles.Single(r => r.Name == RoleDefinitions.Administrator).Id;
            var first = await CreateAsync(seed);
            var second = await CreateAsync(seed);
            var actor = await CreateAsync(seed);
            await SetAccessAsync(seed, first.Id, [administrator], []);
            await SetAccessAsync(seed, second.Id, [administrator], []);
            var permissions = (await seed.GetFromJsonAsync<PermissionCatalogItem[]>("/api/v1/permissions?limit=100"))!;
            await SetAccessAsync(seed, actor.Id, [], permissions.Select(p => p.Id).ToArray());
            using var caller = await LoginAsync(fixture, actor.Email, Password);

            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var seedUser = await db.Users.SingleAsync(u => u.Email == DatabaseSeeder.DefaultAdminEmail);
            Assert.Equal(HttpStatusCode.NoContent, (await caller.PutAsJsonAsync($"/api/v1/users/{seedUser.Id}",
                new { seedUser.Email, seedUser.UserName, seedUser.FullName, isActive = false })).StatusCode);

            await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
            await connection.OpenAsync();
            await using var lockTransaction = await connection.BeginTransactionAsync();
            await using (var command = new NpgsqlCommand($"SELECT pg_advisory_xact_lock({AdministratorUpdateLockKey})", connection, lockTransaction))
                await command.ExecuteNonQueryAsync();

            using var otherCaller = await LoginAsync(fixture, actor.Email, Password);
            var firstTask = ChangeAsync(caller, first, firstAction);
            var secondTask = ChangeAsync(otherCaller, second, secondAction);
            await WaitForAdvisoryWaitersAsync(db.Database.GetConnectionString()!);
            Assert.False(firstTask.IsCompleted);
            Assert.False(secondTask.IsCompleted);
            await lockTransaction.CommitAsync();

            var outcomes = await Task.WhenAll(firstTask, secondTask);
            Assert.Contains(outcomes, response => response.StatusCode == HttpStatusCode.NoContent);
            Assert.Contains(outcomes, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(1, await db.Users.AsNoTracking().CountAsync(u => u.IsActive &&
                db.UserRoles.Any(r => r.UserId == u.Id && r.RoleId == administrator)));
        }
        finally
        {
            await fixture.DisposeAsync();
        }
    }

    private static async Task WaitForAdvisoryWaitersAsync(string connectionString)
    {
        await using var observer = new NpgsqlConnection(connectionString);
        await observer.OpenAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        while (true)
        {
            await using var command = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event = 'advisory' AND query LIKE '%pg_advisory_xact_lock%'", observer);
            if ((long)(await command.ExecuteScalarAsync(timeout.Token))! >= 2) return;
            await Task.Delay(25, timeout.Token);
        }
    }

    private static Task<HttpResponseMessage> ChangeAsync(HttpClient client, UserDto user, string action)
        => action == "deactivate"
            ? client.PutAsJsonAsync($"/api/v1/users/{user.Id}", new { user.Email, user.UserName, user.FullName, isActive = false })
            : client.PutAsJsonAsync($"/api/v1/users/{user.Id}/access", new
            {
                roleIds = Array.Empty<Guid>(),
                overrides = Array.Empty<object>(),
                hasAllWarehousesAccess = true,
                warehouseIds = Array.Empty<Guid>()
            });

    private static async Task<HttpClient> LoginAsync(PostgresApiFixture fixture, string email, string password)
    {
        var client = fixture.Factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResultDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private static async Task<UserDto> CreateAsync(HttpClient client)
    {
        var response = await client.PostAsJsonAsync("/api/v1/users", new
        {
            email = $"{Guid.NewGuid():N}@test.local",
            userName = "user",
            fullName = "Test User",
            password = Password,
            roleIds = Array.Empty<Guid>(),
            hasAllWarehousesAccess = true,
            warehouseScopeIds = Array.Empty<Guid>()
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<UserDto>())!;
    }

    private static async Task SetAccessAsync(HttpClient client, Guid id, Guid[] roles, Guid[] allows)
    {
        var response = await client.PutAsJsonAsync($"/api/v1/users/{id}/access", new
        {
            roleIds = roles,
            overrides = allows.Select(permissionId => new { permissionId, type = 1 }).ToArray(),
            hasAllWarehousesAccess = true,
            warehouseIds = Array.Empty<Guid>()
        });
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    private sealed class ObservingDispatcher(string connectionString) : IDomainEventDispatcher
    {
        public List<Guid> Seen { get; } = [];

        public async Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            foreach (var item in domainEvents.OfType<UserCreatedDomainEvent>())
            {
                await using var command = new NpgsqlCommand("SELECT count(*) FROM users WHERE \"Id\" = @id", connection);
                command.Parameters.AddWithValue("id", item.UserId);
                Assert.Equal(1L, await command.ExecuteScalarAsync(cancellationToken));
                Seen.Add(item.UserId);
            }
        }
    }
}
