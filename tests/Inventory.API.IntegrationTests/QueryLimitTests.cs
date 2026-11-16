using System.Net;
using System.Net.Http.Json;
using Inventory.Domain.Audit;
using Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Audit;
using Shared.Contracts.Catalog;
using Shared.Contracts.Inventory;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class QueryLimitTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Theory]
    [InlineData("movements")]
    [InlineData("kardex-v1")]
    [InlineData("kardex-v2")]
    [InlineData("audit")]
    public async Task Excessive_date_ranges_return_400_and_366_days_remain_valid(string endpoint)
    {
        var helper = new OperationalApiData(fixture.Factory);
        var data = await helper.SeedAsync();
        using var client = await helper.LoginAsync();
        var path = Path(endpoint, data);
        using var boundary = await client.GetAsync(path + "&fromUtc=2024-01-01T00:00:00Z&toUtc=2025-01-01T00:00:00Z");
        Assert.Equal(HttpStatusCode.OK, boundary.StatusCode);
        await OperationalApiData.ProblemAsync(await client.GetAsync(path + "&fromUtc=2024-01-01T00:00:00Z&toUtc=2025-01-01T00:00:01Z"),
            HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("movements")]
    [InlineData("kardex-v2")]
    [InlineData("audit")]
    [InlineData("stock")]
    [InlineData("users")]
    [InlineData("permissions")]
    public async Task Paginated_queries_reject_pages_beyond_1000(string endpoint)
    {
        var helper = new OperationalApiData(fixture.Factory);
        var data = await helper.SeedAsync();
        using var client = await helper.LoginAsync();
        var path = Path(endpoint, data);
        using var boundary = await client.GetAsync(path + "&page=1000&pageSize=1");
        Assert.Equal(HttpStatusCode.OK, boundary.StatusCode);
        await OperationalApiData.ProblemAsync(await client.GetAsync(path + "&page=1001&pageSize=1"), HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Default_and_one_sided_windows_are_bounded_while_kardex_keeps_older_opening_balance()
    {
        var helper = new OperationalApiData(fixture.Factory);
        var data = await helper.SeedAsync();
        using var client = await helper.LoginAsync();
        var old = await OperationalApiData.MoveAsync(client, data, "entries", 10);
        var recent = await OperationalApiData.MoveAsync(client, data, "exits", 2);
        var olderTime = DateTime.UtcNow.AddDays(-500);
        var recentTime = DateTime.UtcNow.AddDays(-1);
        var entity = "Window-" + Guid.NewGuid().ToString("N");
        using (var scope = fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE inventory_movements SET \"CreatedAtUtc\" = {olderTime} WHERE \"Id\" = {old.Id}");
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE inventory_movements SET \"CreatedAtUtc\" = {recentTime} WHERE \"Id\" = {recent.Id}");
            db.AuditLogs.AddRange(new AuditLog(null, olderTime, entity, "Created", null, null, null, null),
                new AuditLog(null, recentTime, entity, "Created", null, null, null, null));
            await db.SaveChangesAsync();
        }
        var movements = await OperationalApiData.PageAsync<MovementSummaryDto>(client, Path("movements", data));
        Assert.Equal(recent.Id, Assert.Single(movements.Items).Id);
        Assert.Equal(1, movements.TotalCount);
        var current = await OperationalApiData.PageAsync<KardexEntryDto>(client, Path("kardex-v2", data));
        Assert.Equal(1, current.TotalCount);
        Assert.Equal(8m, Assert.Single(current.Items).RunningBalance);
        var legacy = (await client.GetFromJsonAsync<KardexEntryDto[]>(Path("kardex-v1", data)))!;
        Assert.Equal(current.Items, legacy);
        var auditPath = "/api/v1/audit-logs?entity=" + entity;
        Assert.Equal(1, (await OperationalApiData.PageAsync<AuditLogDto>(client, auditPath)).TotalCount);
        var from = Uri.EscapeDataString(olderTime.ToString("O"));
        var to = Uri.EscapeDataString(olderTime.AddDays(366).ToString("O"));
        foreach (var window in new[] { "&fromUtc=" + from, "&toUtc=" + from, "&fromUtc=" + from + "&toUtc=" + to })
        {
            var historical = await OperationalApiData.PageAsync<MovementSummaryDto>(client, Path("movements", data) + window);
            Assert.Equal(old.Id, Assert.Single(historical.Items).Id);
            var kardex = await OperationalApiData.PageAsync<KardexEntryDto>(client, Path("kardex-v2", data) + window);
            Assert.Equal(10m, Assert.Single(kardex.Items).RunningBalance);
            Assert.Equal(1, (await OperationalApiData.PageAsync<AuditLogDto>(client, auditPath + window)).TotalCount);
        }
    }

    [Fact]
    public async Task Products_keep_their_existing_clamping_policy_with_the_new_page_ceiling()
    {
        using var client = await new OperationalApiData(fixture.Factory).LoginAsync();
        var page = await OperationalApiData.PageAsync<ProductDto>(client, "/api/v1/products?page=1001&pageSize=1");
        Assert.Equal(1000, page.Page);
    }

    private static string Path(string endpoint, InventoryScenario data) => endpoint switch
    {
        "movements" => $"/api/v1/inventory/movements?productId={data.Product}",
        "kardex-v1" => $"/api/v1/inventory/kardex?productId={data.Product}&warehouseId={data.Source}",
        "kardex-v2" => $"/api/v2/inventory/kardex?productId={data.Product}&warehouseId={data.Source}",
        "audit" => "/api/v1/audit-logs?entity=WindowLimits",
        "stock" => $"/api/v2/inventory/stock?productId={data.Product}",
        "users" => "/api/v1/users?search=WindowLimits",
        "permissions" => "/api/v2/permissions?search=WindowLimits",
        _ => throw new ArgumentOutOfRangeException(nameof(endpoint))
    };
}
