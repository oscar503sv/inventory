using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Inventory.Domain.Inventory;
using Inventory.Persistence;
using Inventory.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;
using Shared.Contracts.Catalog;
using Shared.Contracts.Identity;
using Shared.Contracts.Inventory;
using Shared.Contracts.Warehouses;

namespace Inventory.API.IntegrationTests;

public sealed class InventoryApiTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Fact]
    public async Task Migrations_and_seed_are_complete_and_idempotent()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
        await scope.ServiceProvider.GetRequiredService<DatabaseSeeder>().SeedAsync();
        Assert.Equal(Permissions.All.Count, await db.Permissions.CountAsync());
        Assert.Equal(RoleDefinitions.DefaultPermissions.Count, await db.Roles.CountAsync());
        var admin = await db.Users.Include(u => u.Roles).SingleAsync();
        Assert.Equal(DatabaseSeeder.DefaultAdminEmail, admin.Email);
        Assert.True(admin.IsActive);
        Assert.True(admin.HasAllWarehousesAccess);
        Assert.Single(admin.Roles);
        var expected = RoleDefinitions.DefaultPermissions.Values.Sum(p => p.Length);
        Assert.Equal(expected, await db.RolePermissions.CountAsync());
    }

    [Fact]
    public async Task Login_product_entry_exit_transfer_and_kardex_persist_consistent_balances()
    {
        using var client = fixture.Factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/products")).StatusCode);
        var auth = await Post<AuthResultDto>(client, "/api/v1/auth/login", new
        {
            email = DatabaseSeeder.DefaultAdminEmail, password = DatabaseSeeder.DefaultAdminPassword
        });
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        var category = await Post<CategoryDto>(client, "/api/v1/categories", new { name = "Integration", description = "Test" });
        var brand = await Post<BrandDto>(client, "/api/v1/brands", new { name = "Integration", description = "Test" });
        var unit = await Post<UnitOfMeasureDto>(client, "/api/v1/units-of-measure", new { code = "EA", name = "Each" });
        var product = await Post<ProductDto>(client, "/api/v1/products", new
        {
            sku = "TEST-001", code = "T1", name = "Widget", description = "Integration",
            categoryId = category.Id, brandId = brand.Id, unitOfMeasureId = unit.Id, minimumStock = 5
        });
        var source = await Post<WarehouseDto>(client, "/api/v1/warehouses", new { code = "SRC", name = "Source", address = "A" });
        var destination = await Post<WarehouseDto>(client, "/api/v1/warehouses", new { code = "DST", name = "Destination", address = "B" });
        var entry = await Post<MovementDto>(client, "/api/v1/inventory/entries", new
        {
            warehouseId = source.Id, lines = new[] { new { productId = product.Id, quantity = 100, unitCost = 2 } }
        });
        var exit = await Post<MovementDto>(client, "/api/v1/inventory/exits", new
        {
            warehouseId = source.Id, lines = new[] { new { productId = product.Id, quantity = 30, unitCost = 2 } }
        });
        var transfer = await Post<MovementDto>(client, "/api/v1/inventory/transfers", new
        {
            sourceWarehouseId = source.Id, destinationWarehouseId = destination.Id,
            lines = new[] { new { productId = product.Id, quantity = 20, unitCost = 2 } }
        });

        // A new client and scope read persisted data independently of the write requests.
        using var reader = fixture.Factory.CreateClient();
        reader.DefaultRequestHeaders.Authorization = client.DefaultRequestHeaders.Authorization;
        Assert.Equal(product, await reader.GetFromJsonAsync<ProductDto>($"/api/v1/products/{product.Id}"));
        var stocks = (await reader.GetFromJsonAsync<StockDto[]>($"/api/v1/inventory/stock?productId={product.Id}"))!;
        Assert.Equal(2, stocks.Length);
        Assert.Equal(50m, stocks.Single(s => s.WarehouseId == source.Id).Quantity);
        Assert.Equal(20m, stocks.Single(s => s.WarehouseId == destination.Id).Quantity);
        Assert.All(stocks, s => { Assert.Equal(0m, s.ReservedQuantity); Assert.Equal(s.Quantity, s.AvailableQuantity); });
        var sourceLedger = (await reader.GetFromJsonAsync<KardexEntryDto[]>($"/api/v1/inventory/kardex?productId={product.Id}&warehouseId={source.Id}"))!;
        var destinationLedger = (await reader.GetFromJsonAsync<KardexEntryDto[]>($"/api/v1/inventory/kardex?productId={product.Id}&warehouseId={destination.Id}"))!;
        Assert.Equal(new[] { entry.MovementNumber, exit.MovementNumber, transfer.MovementNumber }, sourceLedger.Select(r => r.MovementNumber));
        Assert.Equal(new[] { "Entry", "Exit", "Transfer" }, sourceLedger.Select(r => r.MovementType));
        Assert.Equal(new decimal[] { 100, -30, -20 }, sourceLedger.Select(r => r.Quantity));
        Assert.Equal(new decimal[] { 100, 70, 50 }, sourceLedger.Select(r => r.RunningBalance));
        var arriving = Assert.Single(destinationLedger);
        Assert.Equal(transfer.MovementNumber, arriving.MovementNumber);
        Assert.Equal(20m, arriving.Quantity);
        Assert.Equal(20m, arriving.RunningBalance);
        foreach (var stock in stocks)
        {
            var ledger = stock.WarehouseId == source.Id ? sourceLedger : destinationLedger;
            Assert.All(ledger, r => Assert.Equal(stock.WarehouseId, r.WarehouseId));
            Assert.Equal(stock.Quantity, ledger.Sum(r => r.Quantity));
            Assert.Equal(stock.Quantity, ledger.Last().RunningBalance);
        }
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var persisted = await db.InventoryMovements.AsNoTracking().Include(m => m.Lines).OrderBy(m => m.CreatedAtUtc).ToListAsync();
        Assert.Equal(new[] { entry.Id, exit.Id, transfer.Id }, persisted.Select(m => m.Id));
        Assert.All(persisted, m =>
        {
            Assert.Equal(auth.UserId, m.CreatedBy);
            Assert.Equal(MovementStatus.Confirmed, m.Status);
            var line = Assert.Single(m.Lines);
            Assert.Equal(product.Id, line.ProductId);
            Assert.Equal(2m, line.UnitCost);
        });
        Assert.Equal(new decimal[] { 100, 30, 20 }, persisted.Select(m => m.Lines.Single().Quantity));
        Assert.Equal(destination.Id, persisted.Last().DestinationWarehouseId);
        Assert.Equal(2, await db.Stocks.CountAsync());
    }

    private static async Task<T> Post<T>(HttpClient client, string path, object body)
    {
        using var response = await client.PostAsJsonAsync(path, body);
        var content = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"POST {path}: {(int)response.StatusCode} {content}");
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }
}
