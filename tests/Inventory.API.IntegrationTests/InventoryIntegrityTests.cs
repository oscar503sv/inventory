using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Features.Inventory;
using Inventory.Domain.Catalog;
using Inventory.Domain.Identity;
using Inventory.Domain.Warehouses;
using Inventory.Persistence;
using Inventory.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;
using Shared.Contracts.Identity;
using Shared.Contracts.Inventory;
using Shared.Contracts.Warehouses;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class InventoryIntegrityTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    private const string Password = "Regression#12345";

    [Theory]
    [InlineData(false, false, false)] // role + one warehouse
    [InlineData(false, true, false)]  // Allow without a role
    [InlineData(true, false, false)]  // global scope
    [InlineData(true, false, true)]   // Deny overrides role, including global scope
    public async Task Reads_and_writes_enforce_permissions_and_warehouse_scope(bool global, bool allow, bool deny)
    {
        var data = await Seed();
        using var admin = await Login();
        await Send(admin, "entries", Entry(data, 10));
        await Send(admin, "entries", Entry(data, 20) with { WarehouseId = data.Destination });
        var email = await CreateUser(data.Source, global, allow, deny);
        using var client = await Login(email);
        var stockPath = $"/api/v1/inventory/stock?productId={data.Product}";
        var kardexPath = $"/api/v1/inventory/kardex?productId={data.Product}&warehouseId=";
        if (deny)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(stockPath)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(kardexPath + data.Source)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await client.PostAsJsonAsync("/api/v1/inventory/entries", Entry(data, 1))).StatusCode);
            return;
        }
        var stocks = (await client.GetFromJsonAsync<StockDto[]>(stockPath))!;
        Assert.Equal(global ? 2 : 1, stocks.Length);
        Assert.Contains(stocks, s => s.WarehouseId == data.Source);
        var warehouses = (await client.GetFromJsonAsync<WarehouseDto[]>("/api/v1/warehouses"))!;
        Assert.Contains(warehouses, w => w.Id == data.Source);
        if (!global) Assert.Single(warehouses);
        else Assert.Contains(warehouses, w => w.Id == data.Destination);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(kardexPath + data.Source)).StatusCode);
        Assert.Equal(global ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await client.GetAsync(stockPath + "&warehouseId=" + data.Destination)).StatusCode);
        Assert.Equal(global ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await client.GetAsync(kardexPath + data.Destination)).StatusCode);
        await Send(client, "entries", Entry(data, 1));
        var transfer = new CreateTransferCommand(data.Source, data.Destination,
            [new(data.Product, 1)], IdempotencyKey: Guid.NewGuid());
        Assert.Equal(global ? HttpStatusCode.OK : HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/v1/inventory/transfers", transfer)).StatusCode);
        if (!global)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/v1/inventory/transfers",
                transfer with { SourceWarehouseId = data.Destination, DestinationWarehouseId = data.Source })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.PutAsJsonAsync($"/api/v1/warehouses/{data.Destination}",
                new { name = "Blocked", address = "Blocked" })).StatusCode);
        }
    }

    [Fact]
    public async Task User_without_scopes_sees_empty_lists_and_cannot_read_explicit_warehouses()
    {
        var data = await Seed();
        using var client = await Login(await CreateUser(null, false, true, false));
        Assert.Empty((await client.GetFromJsonAsync<StockDto[]>("/api/v1/inventory/stock"))!);
        Assert.Empty((await client.GetFromJsonAsync<WarehouseDto[]>("/api/v1/warehouses"))!);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/v1/inventory/stock?warehouseId={data.Source}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync($"/api/v1/inventory/kardex?productId={data.Product}&warehouseId={data.Source}")).StatusCode);
    }

    [Fact]
    public async Task Replay_still_requires_current_warehouse_access()
    {
        var data = await Seed();
        var email = await CreateUser(data.Source, false, true, false);
        using var client = await Login(email);
        var request = Entry(data, 10);
        await Send(client, "entries", request);
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var user = await db.Users.SingleAsync(u => u.Email == email);
            await db.WarehouseScopes.Where(s => s.UserId == user.Id).ExecuteDeleteAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync("/api/v1/inventory/entries", request)).StatusCode);
    }

    [Fact]
    public async Task Audit_excludes_secrets_on_create_update_delete_and_token_rotation()
    {
        var email = await CreateUser(null, false, true, false);
        using var client = await Login(email);
        var login = await client.PostAsJsonAsync("/api/v1/auth/login", new { email, password = Password });
        var auth = (await login.Content.ReadFromJsonAsync<AuthResultDto>())!;
        var refresh = await client.PostAsJsonAsync("/api/v1/auth/refresh", new { refreshToken = auth.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users.SingleAsync(u => u.Email == email);
        var oldHash = user.PasswordHash;
        const string newHash = "private-new-password-hash";
        user.ChangePassword(newHash);
        await db.SaveChangesAsync();
        db.Users.Remove(user);
        db.SaveChanges(); // synchronous interceptor path, including deleted original values
        var logs = await db.AuditLogs.AsNoTracking().ToListAsync();
        var userLogs = logs.Where(l => l.Entity == "User" && ((l.NewValues ?? "") + (l.OldValues ?? "")).Contains(email)).ToList();
        Assert.Equal(new[] { "Created", "Deleted", "Updated" }, userLogs.Select(l => l.Action).Order());
        foreach (var log in logs)
        foreach (var json in new[] { log.OldValues, log.NewValues }.OfType<string>())
        {
            Assert.DoesNotContain(oldHash, json);
            Assert.DoesNotContain(newHash, json);
            Assert.DoesNotContain(Password, json);
            Assert.DoesNotContain(auth.AccessToken, json);
            Assert.DoesNotContain(auth.RefreshToken, json);
            foreach (var property in JsonDocument.Parse(json).RootElement.EnumerateObject())
                Assert.DoesNotMatch("(?i)password|hash|token|secret", property.Name);
        }
    }

    [Theory]
    [InlineData("entries")]
    [InlineData("exits")]
    [InlineData("transfers")]
    [InlineData("adjustments")]
    public async Task Retry_returns_original_movement_and_reusing_key_with_different_payload_conflicts(string operation)
    {
        var data = await Seed();
        using var client = await Login();
        await Send(client, "entries", Entry(data, 100));
        var key = Guid.NewGuid();
        var request = Request(operation, data, key, [new(data.Product, 20)]);
        var first = await Send(client, operation, request);
        var second = await Send(client, operation, request);
        Assert.Equal(first with { Lines = second.Lines }, second);
        Assert.Equal(first.Lines, second.Lines);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/inventory/" + operation,
            Request(operation, data, key, [new(data.Product, 21)]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/v1/inventory/entries",
            Entry(data, 2) with { IdempotencyKey = key })).StatusCode);
        await AssertBalances(client, data, operation switch { "entries" => 120, "exits" or "transfers" => 80, _ => 20 },
            operation == "transfers" ? 20 : null, 2);
    }

    [Theory]
    [InlineData("entries")]
    [InlineData("exits")]
    [InlineData("transfers")]
    [InlineData("adjustments")]
    public async Task Duplicate_products_missing_keys_and_excess_precision_are_rejected_without_writes(string operation)
    {
        var data = await Seed();
        using var client = await Login();
        await Send(client, "entries", Entry(data, 100));
        var duplicate = Request(operation, data, Guid.NewGuid(), [new(data.Product, 10), new(data.Product, 20)]);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/inventory/" + operation, duplicate)).StatusCode);
        var missingKey = Request(operation, data, Guid.Empty, [new(data.Product, 10)]);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/inventory/" + operation, missingKey)).StatusCode);
        var excessPrecision = Request(operation, data, Guid.NewGuid(), [new(data.Product, 0.00005m)]);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/inventory/" + operation, excessPrecision)).StatusCode);
        var excessCostPrecision = Request(operation, data, Guid.NewGuid(), [new(data.Product, 1, 0.00005m)]);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/v1/inventory/" + operation, excessCostPrecision)).StatusCode);
        await AssertBalances(client, data, 100, null, 1);
    }

    [Fact]
    public async Task Numerically_identical_decimal_values_share_the_same_idempotency_fingerprint()
    {
        var data = await Seed();
        using var client = await Login();
        var request = Entry(data, 10);
        var first = await Send(client, "entries", request);
        var replay = await Send(client, "entries", request with { Lines = [new(data.Product, 10.0000m, 0.0m)] });
        Assert.Equal(first.Id, replay.Id);
        await AssertBalances(client, data, 10, null, 1);
    }

    [Fact]
    public async Task Idempotency_keys_are_scoped_to_the_authenticated_user()
    {
        var data = await Seed();
        using var admin = await Login();
        using var other = await Login(await CreateUser(data.Source, false, true, false));
        var request = Entry(data, 10);
        var first = await Send(admin, "entries", request);
        var second = await Send(other, "entries", request);
        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.CreatedBy, second.CreatedBy);
        Assert.Equal(first.Id, (await Send(admin, "entries", request)).Id);
        Assert.Equal(second.Id, (await Send(other, "entries", request)).Id);
        await AssertBalances(admin, data, 20, null, 2);
    }

    [Fact]
    public async Task Concurrent_adjustment_and_exit_reconstruct_the_committed_order()
    {
        var data = await Seed();
        using var client = await Login();
        await Send(client, "entries", Entry(data, 100));
        var adjustment = Request("adjustments", data, Guid.NewGuid(), [new(data.Product, 50)]);
        var exit = Request("exits", data, Guid.NewGuid(), [new(data.Product, 10)]);
        fixture.SaveGate.Arm(data.Product);
        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/v1/inventory/adjustments", adjustment),
            client.PostAsJsonAsync("/api/v1/inventory/exits", exit));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var adjustmentWon = responses[0].IsSuccessStatusCode;
        await Send(client, "adjustments", adjustment);
        await Send(client, "exits", exit);
        await AssertBalances(client, data, adjustmentWon ? 40 : 50, null, 3);
    }

    [Theory]
    [InlineData("entries", false)]
    [InlineData("entries", true)]
    [InlineData("exits", false)]
    [InlineData("transfers", false)]
    [InlineData("transfers", true)]
    [InlineData("adjustments", false)]
    public async Task Simultaneous_movements_conflict_atomically_and_can_be_retried(string operation, bool newStock)
    {
        var data = await Seed();
        using var client = await Login();
        var initial = operation == "entries" && newStock ? 0 : 100;
        if (initial != 0) await Send(client, "entries", Entry(data, initial));
        if (operation == "transfers" && !newStock)
            await Send(client, "entries", Entry(data, 1) with { WarehouseId = data.Destination });
        var quantity = operation == "adjustments" ? 100 : 10; // no-op counts must also advance the version
        var requests = Enumerable.Range(0, 2).Select(_ => Request(operation, data, Guid.NewGuid(), [new(data.Product, quantity)])).ToArray();
        fixture.SaveGate.Arm(data.Product);
        var responses = await Task.WhenAll(requests.Select(r => client.PostAsJsonAsync("/api/v1/inventory/" + operation, r)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var failed = Array.FindIndex(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        await Send(client, operation, requests[failed]);
        // Retrying both, including the already committed request, must never add another movement.
        foreach (var request in requests) await Send(client, operation, request);
        var expected = operation switch { "entries" => initial + 20, "adjustments" => 100, _ => 80 };
        await AssertBalances(client, data, expected, operation == "transfers" ? newStock ? 20 : 21 : null,
            (initial == 0 ? 0 : 1) + (operation == "transfers" && !newStock ? 1 : 0) + 2);
    }

    [Fact]
    public async Task Simultaneous_duplicate_requests_persist_only_one_movement()
    {
        var data = await Seed();
        using var client = await Login();
        var request = Entry(data, 10);
        fixture.SaveGate.Arm(data.Product);
        var responses = await Task.WhenAll(Enumerable.Range(0, 2)
            .Select(_ => client.PostAsJsonAsync("/api/v1/inventory/entries", request)));
        var success = Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        var first = (await success.Content.ReadFromJsonAsync<MovementDto>())!;
        var replay = await Send(client, "entries", request);
        Assert.Equal(first.Id, replay.Id);
        await AssertBalances(client, data, 10, null, 1);
    }

    [Fact]
    public async Task Simultaneous_exits_cannot_oversell_even_after_retry()
    {
        var data = await Seed();
        using var client = await Login();
        await Send(client, "entries", Entry(data, 10));
        var requests = Enumerable.Range(0, 2).Select(_ => Request("exits", data, Guid.NewGuid(), [new(data.Product, 7)])).ToArray();
        fixture.SaveGate.Arm(data.Product);
        var responses = await Task.WhenAll(requests.Select(r => client.PostAsJsonAsync("/api/v1/inventory/exits", r)));
        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.OK);
        var failed = Array.FindIndex(responses, r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.True(failed >= 0);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await client.PostAsJsonAsync("/api/v1/inventory/exits", requests[failed])).StatusCode);
        await AssertBalances(client, data, 3, null, 2);
    }

    [Theory]
    [InlineData("transfers")]
    [InlineData("adjustments")]
    public async Task Failure_on_later_line_does_not_persist_any_partial_changes(string operation)
    {
        var data = await Seed();
        using var client = await Login();
        await Send(client, "entries", Entry(data, 100) with { Lines = [new(data.Product, 100), new(data.OtherProduct, 5)] });
        if (operation == "adjustments")
        {
            using var scope = fixture.Factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reserved = await db.Stocks.SingleAsync(s => s.ProductId == data.OtherProduct && s.WarehouseId == data.Source);
            reserved.Reserve(4);
            await db.SaveChangesAsync();
        }
        var key = Guid.NewGuid();
        var request = Request(operation, data, key, [new(data.Product, 10), new(data.OtherProduct, operation == "transfers" ? 6 : 3)]);
        Assert.Equal(HttpStatusCode.UnprocessableEntity,
            (await client.PostAsJsonAsync("/api/v1/inventory/" + operation, request)).StatusCode);
        await AssertBalances(client, data, 100, null, 1);
        var otherStock = (await client.GetFromJsonAsync<StockDto[]>($"/api/v1/inventory/stock?productId={data.OtherProduct}"))!;
        Assert.Equal(5m, Assert.Single(otherStock).Quantity);
        // Failed operations do not consume their key.
        await Send(client, operation, Request(operation, data, key, [new(data.Product, 10), new(data.OtherProduct, 4)]));
        await AssertBalances(client, data, operation == "transfers" ? 90 : 10, operation == "transfers" ? 10 : null, 2);
    }

    [Fact]
    public async Task Kardex_uses_persisted_sequence_even_when_clock_moves_backwards()
    {
        var data = await Seed();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddSingleton<IDateTimeProvider, BackwardsClock>()));
        using var client = factory.CreateClient();
        await Authenticate(client, DatabaseSeeder.DefaultAdminEmail);
        var entry = await Send(client, "entries", Entry(data, 100));
        var adjustment = await Send(client, "adjustments", Request("adjustments", data, Guid.NewGuid(), [new(data.Product, 50)]));
        var exit = await Send(client, "exits", Request("exits", data, Guid.NewGuid(), [new(data.Product, 10)]));
        Assert.True(entry.CreatedAtUtc > adjustment.CreatedAtUtc && adjustment.CreatedAtUtc > exit.CreatedAtUtc);
        await AssertBalances(client, data, 40, null, 3);
    }

    [Theory]
    [InlineData("40001")]
    [InlineData("40P01")]
    public async Task Postgres_transient_write_errors_return_conflict_and_roll_back(string sqlState)
    {
        var data = await Seed();
        using var client = await Login();
        await Send(client, "entries", Entry(data, 100));
        var request = Request("transfers", data, Guid.NewGuid(), [new(data.Product, 10)]);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Inject the server error during a write, after SaveChanges has begun its transaction.
        // This exercises Npgsql's real exception wrapping and PostgreSQL rollback through HTTP.
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION test_inventory_conflict() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'Injected inventory conflict' USING ERRCODE = TG_ARGV[0]; END $$;
            """);
        // Only test-generated GUIDs and the two constant SQLSTATE values are interpolated in DDL.
        var triggerSql = $"""
            CREATE TRIGGER test_inventory_conflict BEFORE UPDATE ON stocks FOR EACH ROW
            WHEN (NEW."WarehouseId" = '{data.Source:D}'::uuid)
            EXECUTE FUNCTION test_inventory_conflict('{sqlState}');
            """;
        try
        {
            await db.Database.ExecuteSqlRawAsync(triggerSql);
            Assert.Equal(HttpStatusCode.Conflict,
                (await client.PostAsJsonAsync("/api/v1/inventory/transfers", request)).StatusCode);
            await AssertBalances(client, data, 100, null, 1);
        }
        finally
        {
            await db.Database.ExecuteSqlRawAsync("DROP TRIGGER IF EXISTS test_inventory_conflict ON stocks; DROP FUNCTION test_inventory_conflict();");
        }
        await Send(client, "transfers", request);
        await AssertBalances(client, data, 90, 10, 2);
    }

    private sealed class BackwardsClock : IDateTimeProvider
    {
        private long _ticks = DateTime.UtcNow.Ticks;
        public DateTime UtcNow => new(Interlocked.Add(ref _ticks, -TimeSpan.TicksPerSecond), DateTimeKind.Utc);
    }

    private sealed record Data(Guid Product, Guid OtherProduct, Guid Source, Guid Destination);

    private async Task<Data> Seed()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var suffix = Guid.NewGuid().ToString("N")[..12];
        var category = new Category("C-" + suffix, "");
        var brand = new Brand("B-" + suffix, "");
        var unit = new UnitOfMeasure(suffix, "Each");
        var source = Warehouse.Create("S-" + suffix, "Source", "");
        var destination = Warehouse.Create("D-" + suffix, "Destination", "");
        var product = Product.Create("P-" + suffix, "P", "Product", "", category.Id, brand.Id, unit.Id, 0);
        var other = Product.Create("O-" + suffix, "O", "Other", "", category.Id, brand.Id, unit.Id, 0);
        db.AddRange(category, brand, unit, source, destination, product, other);
        await db.SaveChangesAsync();
        return new(product.Id, other.Id, source.Id, destination.Id);
    }

    private async Task<string> CreateUser(Guid? warehouse, bool global, bool allow, bool deny)
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = $"test-{Guid.NewGuid():N}@inventory.local";
        var hash = scope.ServiceProvider.GetRequiredService<IPasswordHasher>().Hash(Password);
        var user = User.Create(email, email, "Regression", hash, global);
        if (!allow) user.AssignRole((await db.Roles.SingleAsync(r => r.Name == RoleDefinitions.Administrator)).Id);
        var codes = new[] { Permissions.Inventory.Read, Permissions.Inventory.EntryCreate,
            Permissions.Inventory.TransferCreate, Permissions.Users.Manage };
        var permissions = await db.Permissions.Where(p => codes.Contains(p.Code)).ToListAsync();
        foreach (var permission in permissions)
            if (allow || deny) user.SetOverride(permission.Id, deny ? OverrideType.Deny : OverrideType.Allow);
        if (warehouse is { } id) user.GrantWarehouseScope(id);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return email;
    }

    private async Task<HttpClient> Login(string? email = null)
    {
        var client = fixture.Factory.CreateClient();
        await Authenticate(client, email ?? DatabaseSeeder.DefaultAdminEmail);
        return client;
    }

    private static async Task Authenticate(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/api/v1/auth/login", new
        {
            email, password = email == DatabaseSeeder.DefaultAdminEmail ? DatabaseSeeder.DefaultAdminPassword : Password
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var auth = (await response.Content.ReadFromJsonAsync<AuthResultDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
    }

    private static CreateEntryCommand Entry(Data data, decimal quantity)
        => new(data.Source, [new(data.Product, quantity)], IdempotencyKey: Guid.NewGuid());

    private static object Request(string operation, Data data, Guid key, IReadOnlyList<MovementLineInput> lines) => operation switch
    {
        "entries" => new CreateEntryCommand(data.Source, lines, IdempotencyKey: key),
        "exits" => new CreateExitCommand(data.Source, lines, IdempotencyKey: key),
        "transfers" => new CreateTransferCommand(data.Source, data.Destination, lines, IdempotencyKey: key),
        "adjustments" => new CreateAdjustmentCommand(data.Source, lines, "Physical count", key),
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static async Task<MovementDto> Send(HttpClient client, string operation, object request)
    {
        using var response = await client.PostAsJsonAsync("/api/v1/inventory/" + operation, request);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{operation}: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<MovementDto>())!;
    }

    private async Task AssertBalances(HttpClient client, Data data, decimal source, decimal? destination, int movements)
    {
        var stocks = (await client.GetFromJsonAsync<StockDto[]>($"/api/v1/inventory/stock?productId={data.Product}"))!;
        Assert.Equal(destination.HasValue ? 2 : 1, stocks.Length);
        Assert.Equal(source, stocks.Single(s => s.WarehouseId == data.Source).Quantity);
        if (destination.HasValue) Assert.Equal(destination.Value, stocks.Single(s => s.WarehouseId == data.Destination).Quantity);
        foreach (var stock in stocks)
        {
            var ledger = (await client.GetFromJsonAsync<KardexEntryDto[]>(
                $"/api/v1/inventory/kardex?productId={data.Product}&warehouseId={stock.WarehouseId}"))!;
            Assert.Equal(stock.Quantity, ledger.Last().RunningBalance);
            Assert.Equal(stock.Quantity, ledger.Sum(l => l.Quantity));
            Assert.All(ledger, l => Assert.True(l.RunningBalance >= 0));
        }
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.InventoryMovements.AsNoTracking().Where(m => m.Lines.Any(l => l.ProductId == data.Product)).ToListAsync();
        Assert.Equal(movements, persisted.Count);
        var numbers = persisted.Select(m => m.MovementNumber).ToArray();
        var audits = await db.AuditLogs.AsNoTracking().Where(a => a.Entity == "InventoryMovement").ToListAsync();
        Assert.Equal(movements, audits.Count(a => numbers.Any(n => (a.NewValues ?? "").Contains(n))));
        // No orphan audit or movement from a failed write may mention these warehouses.
        Assert.Equal(movements, audits.Count(a => (a.NewValues ?? "").Contains(data.Source.ToString())
            || (a.NewValues ?? "").Contains(data.Destination.ToString())));
    }
}
