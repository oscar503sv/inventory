using System.Net;
using System.Net.Http.Json;
using Inventory.API.Controllers;
using Inventory.Application.Common.Abstractions;
using Inventory.Domain.Catalog;
using Inventory.Domain.Inventory;
using Inventory.Persistence;
using Inventory.Persistence.Seed;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Common;
using Shared.Contracts.Inventory;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class LegacyInventoryQueryTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Fact]
    public async Task Legacy_kardex_is_capped_and_announces_its_successor_and_retirement()
    {
        var helper = new OperationalApiData(fixture.Factory);
        var data = await helper.SeedAsync();
        using var client = await helper.LoginAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actor = await db.Users.Where(user => user.Email == DatabaseSeeder.DefaultAdminEmail).Select(user => user.Id).SingleAsync();
        var prefix = Guid.NewGuid().ToString("N")[..8];
        for (var i = 0; i < 205; i++)
        {
            var movement = InventoryMovement.CreateEntry($"LEG-{prefix}-{i:D4}", data.Source, actor, DateTime.UtcNow,
                [new(data.Product, 1, 0)]);
            movement.SetIdempotency(Guid.NewGuid(), new string('A', 64));
            db.InventoryMovements.Add(movement);
        }
        var stock = new Stock(data.Product, data.Source);
        stock.Increase(205);
        db.Stocks.Add(stock);
        await db.SaveChangesAsync();
        var query = $"?productId={data.Product}&warehouseId={data.Source}";
        using var legacy = await client.GetAsync("/api/v1/inventory/kardex" + query);
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        var rows = (await legacy.Content.ReadFromJsonAsync<KardexEntryDto[]>())!;
        Assert.Equal(200, rows.Length);
        Assert.Equal(200, rows[^1].RunningBalance);
        AssertDeprecation(legacy, "kardex");
        using var next = await client.GetAsync("/api/v2/inventory/kardex" + query + "&page=2&pageSize=200");
        var page = (await next.Content.ReadFromJsonAsync<PagedResult<KardexEntryDto>>())!;
        Assert.Equal(205, page.TotalCount);
        Assert.Equal(5, page.Items.Count);
        Assert.Equal(205, page.Items[^1].RunningBalance);
        Assert.False(next.Headers.Contains("Deprecation"));
    }

    [Fact]
    public async Task Legacy_stock_is_capped_while_v2_exposes_remaining_rows()
    {
        var helper = new OperationalApiData(fixture.Factory);
        var data = await helper.SeedAsync();
        using var client = await helper.LoginAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var template = await db.Products.SingleAsync(product => product.Id == data.Product);
        for (var i = 0; i < 205; i++)
        {
            var product = Product.Create("L" + Guid.NewGuid().ToString("N")[..16], "P", "Legacy", "",
                template.CategoryId, template.BrandId, template.UnitOfMeasureId, 0);
            db.Products.Add(product);
            var stock = new Stock(product.Id, data.Source);
            stock.Increase(1);
            db.Stocks.Add(stock);
        }
        await db.SaveChangesAsync();
        using var legacy = await client.GetAsync($"/api/v1/inventory/stock?warehouseId={data.Source}");
        Assert.Equal(200, (await legacy.Content.ReadFromJsonAsync<StockDto[]>())!.Length);
        AssertDeprecation(legacy, "stock");
        var page = (await client.GetFromJsonAsync<PagedResult<StockDto>>($"/api/v2/inventory/stock?warehouseId={data.Source}&page=2&pageSize=200"))!;
        Assert.Equal(205, page.TotalCount);
        Assert.Equal(5, page.Items.Count);
    }

    [Theory]
    [InlineData("fromUtc=not-a-date")]
    [InlineData("fromUtc=2026-01-01")]
    [InlineData("fromUtc=2026-02-02T00:00:00Z&toUtc=2026-01-01T00:00:00Z")]
    public async Task Legacy_date_inputs_are_validated_like_v2(string dates)
    {
        var helper = new OperationalApiData(fixture.Factory);
        var data = await helper.SeedAsync();
        using var client = await helper.LoginAsync();
        foreach (var version in new[] { "v1", "v2" })
            await OperationalApiData.ProblemAsync(await client.GetAsync($"/api/{version}/inventory/kardex?productId={data.Product}&warehouseId={data.Source}&{dates}"),
                HttpStatusCode.BadRequest);
    }

    private static void AssertDeprecation(HttpResponseMessage response, string successor)
    {
        Assert.Equal("@" + LegacyInventoryDeprecation.DeprecatedAt.ToUnixTimeSeconds(), Assert.Single(response.Headers.GetValues("Deprecation")));
        Assert.Equal(LegacyInventoryDeprecation.SunsetAt.ToString("R"), Assert.Single(response.Headers.GetValues("Sunset")));
        Assert.Contains("/api/v2/inventory/" + successor, Assert.Single(response.Headers.GetValues("Link")));
    }
}
