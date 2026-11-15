using System.Net;
using System.Net.Http.Json;
using Inventory.Application.Common.Abstractions;
using Inventory.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Authorization;
using Shared.Contracts.Catalog;
using Shared.Contracts.Inventory;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class KardexPaginationTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Fact]
    public async Task test_kardex_pages_and_date_filters_keep_opening_balances_across_adjustments()
    {
        var clock = new ManualClock();
        using var factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IDateTimeProvider>(clock)));
        var helper = new OperationalApiData(factory);
        var data = await helper.SeedAsync();
        using var admin = await helper.LoginAsync();
        var history = await RecordHistory(admin, data, clock);
        var path = $"/api/v2/inventory/kardex?productId={data.Product}&warehouseId={data.Source}";
        var from = Uri.EscapeDataString(history[2].CreatedAtUtc.ToString("O"));
        var to = Uri.EscapeDataString(history[5].CreatedAtUtc.ToString("O"));
        var filtered = path + $"&fromUtc={from}&toUtc={to}&pageSize=1";
        var first = await OperationalApiData.PageAsync<KardexEntryDto>(admin, filtered);
        var third = await OperationalApiData.PageAsync<KardexEntryDto>(admin, filtered + "&page=3");
        var last = await OperationalApiData.PageAsync<KardexEntryDto>(admin, filtered + "&page=4");
        Assert.Equal(4, first.TotalCount);
        Assert.Equal(-30m, Assert.Single(first.Items).Quantity);
        Assert.Equal(50m, first.Items[0].RunningBalance);
        Assert.Equal(55m, Assert.Single(third.Items).RunningBalance);
        Assert.Equal(15m, Assert.Single(last.Items).Quantity);
        Assert.Equal(70m, last.Items[0].RunningBalance);
        var all = await OperationalApiData.PageAsync<KardexEntryDto>(admin, path + "&pageSize=200");
        Assert.Equal(new decimal[] { 100, 80, 50, 60, 55, 70, 60 }, all.Items.Select(r => r.RunningBalance));
        var legacy = (await admin.GetFromJsonAsync<KardexEntryDto[]>(path.Replace("/v2/", "/v1/")))!;
        Assert.Equal(legacy, all.Items);
        var empty = await OperationalApiData.PageAsync<KardexEntryDto>(admin, filtered + "&page=5");
        Assert.Empty(empty.Items);
        Assert.Equal(4, empty.TotalCount);
        var destination = await OperationalApiData.PageAsync<KardexEntryDto>(admin,
            $"/api/v2/inventory/kardex?productId={data.Product}&warehouseId={data.Destination}");
        Assert.Equal(10m, Assert.Single(destination.Items).RunningBalance);
    }

    [Fact]
    public async Task test_kardex_excluded_adjustment_still_affects_balance_when_clock_moves_backwards()
    {
        var clock = new ManualClock();
        using var factory = fixture.Factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IDateTimeProvider>(clock)));
        var helper = new OperationalApiData(factory);
        var data = await helper.SeedAsync();
        using var admin = await helper.LoginAsync();
        var entry = await OperationalApiData.MoveAsync(admin, data, "entries", 100);
        clock.Now = clock.Now.AddMinutes(-2);
        await OperationalApiData.MoveAsync(admin, data, "adjustments", 50);
        clock.Now = clock.Now.AddMinutes(1);
        var exit = await OperationalApiData.MoveAsync(admin, data, "exits", 10);
        var from = Uri.EscapeDataString(exit.CreatedAtUtc.ToString("O"));
        var path = $"/api/v2/inventory/kardex?productId={data.Product}&warehouseId={data.Source}&fromUtc={from}&pageSize=1";
        var first = await OperationalApiData.PageAsync<KardexEntryDto>(admin, path);
        var second = await OperationalApiData.PageAsync<KardexEntryDto>(admin, path + "&page=2");
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(entry.MovementNumber, Assert.Single(first.Items).MovementNumber);
        Assert.Equal(exit.MovementNumber, Assert.Single(second.Items).MovementNumber);
        Assert.Equal(40m, second.Items[0].RunningBalance);
    }

    [Fact]
    public async Task test_product_pages_with_equal_names_use_stable_uuid_order()
    {
        var helper = new OperationalApiData(fixture.Factory);
        var data = await helper.SeedAsync();
        var name = "Paged-" + Guid.NewGuid().ToString("N");
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var products = await db.Products.Where(p => p.Id == data.Product || p.Id == data.OtherProduct).ToListAsync();
            foreach (var product in products)
                product.Update(name, "", product.CategoryId, product.BrandId, product.UnitOfMeasureId, product.MinimumStock);
            await db.SaveChangesAsync();
        }
        using var admin = await helper.LoginAsync();
        var path = $"/api/v1/products?search={name}";
        var first = await OperationalApiData.PageAsync<ProductDto>(admin, path + "&pageSize=1");
        var second = await OperationalApiData.PageAsync<ProductDto>(admin, path + "&pageSize=1&page=2");
        var all = await OperationalApiData.PageAsync<ProductDto>(admin, path + "&pageSize=200");
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(2, second.TotalCount);
        Assert.Equal(all.Items.Select(p => p.Id), first.Items.Concat(second.Items).Select(p => p.Id));
        Assert.Equal(all.Items.Select(p => p.Id.ToString()).Order(StringComparer.Ordinal), all.Items.Select(p => p.Id.ToString()));
    }

    [Fact]
    public async Task test_stock_pagination_counts_only_authorized_warehouses()
    {
        var helper = new OperationalApiData(fixture.Factory);
        var data = await helper.SeedAsync();
        using var admin = await helper.LoginAsync();
        await OperationalApiData.MoveAsync(admin, data, "entries", 10);
        await OperationalApiData.MoveAsync(admin, data, "entries", 20, product: data.OtherProduct);
        await OperationalApiData.MoveAsync(admin, data, "entries", 30, data.Destination);
        var user = await helper.UserAsync([Permissions.Inventory.Read], [data.Source]);
        using var scoped = await helper.LoginAsync(user);
        var path = $"/api/v2/inventory/stock?warehouseId={data.Source}&pageSize=1";
        var first = await OperationalApiData.PageAsync<StockDto>(scoped, path);
        var second = await OperationalApiData.PageAsync<StockDto>(scoped, path + "&page=2");
        Assert.Equal(2, first.TotalCount);
        Assert.Equal(2, second.TotalCount);
        Assert.NotEqual(Assert.Single(first.Items).ProductId, Assert.Single(second.Items).ProductId);
        Assert.All(first.Items.Concat(second.Items), row => Assert.Equal(data.Source, row.WarehouseId));
        var unfiltered = await OperationalApiData.PageAsync<StockDto>(scoped, "/api/v2/inventory/stock");
        Assert.Equal(2, unfiltered.TotalCount);
        var global = await OperationalApiData.PageAsync<StockDto>(admin, $"/api/v2/inventory/stock?productId={data.Product}");
        Assert.Equal(2, global.TotalCount);
        await OperationalApiData.ProblemAsync(await scoped.GetAsync($"/api/v2/inventory/stock?warehouseId={data.Destination}"), HttpStatusCode.Forbidden);
        await OperationalApiData.ProblemAsync(await scoped.GetAsync($"/api/v2/inventory/kardex?productId={data.Product}&warehouseId={data.Destination}"), HttpStatusCode.Forbidden);
    }

    private static async Task<List<MovementDto>> RecordHistory(HttpClient client, InventoryScenario data, ManualClock clock)
    {
        var rows = new List<MovementDto>();
        foreach (var (operation, quantity) in new[]
        {
            ("entries", 100m), ("exits", 20m), ("adjustments", 50m), ("entries", 10m),
            ("exits", 5m), ("adjustments", 70m), ("transfers", 10m)
        })
        {
            clock.Now = clock.Now.AddMinutes(1);
            rows.Add(await OperationalApiData.MoveAsync(client, data, operation, quantity));
        }
        return rows;
    }

    private sealed class ManualClock : IDateTimeProvider
    {
        public DateTime Now { get; set; } = DateTime.UtcNow;
        public DateTime UtcNow => Now;
    }
}
