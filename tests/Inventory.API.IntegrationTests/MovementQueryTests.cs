using System.Net;
using System.Net.Http.Json;
using Shared.Authorization;
using Shared.Contracts.Inventory;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class MovementQueryTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    private readonly OperationalApiData _data = new(fixture.Factory);

    [Fact]
    public async Task test_movements_pagination_preserves_order_and_detail()
    {
        // Arrange
        var data = await _data.SeedAsync();
        using var admin = await _data.LoginAsync();
        var entry = await OperationalApiData.MoveAsync(admin, data, "entries", 100);
        var exit = await OperationalApiData.MoveAsync(admin, data, "exits", 10);
        var transfer = await OperationalApiData.MoveAsync(admin, data, "transfers", 20);
        var path = $"/api/v1/inventory/movements?productId={data.Product}&pageSize=1";
        // Act
        var pages = new[]
        {
            await OperationalApiData.PageAsync<MovementSummaryDto>(admin, path),
            await OperationalApiData.PageAsync<MovementSummaryDto>(admin, path + "&page=2"),
            await OperationalApiData.PageAsync<MovementSummaryDto>(admin, path + "&page=3")
        };
        // Assert
        Assert.All(pages, p => { Assert.Equal(3, p.TotalCount); Assert.Equal(3, p.TotalPages); Assert.Single(p.Items); });
        Assert.Equal(new[] { transfer.Id, exit.Id, entry.Id }, pages.SelectMany(p => p.Items).Select(m => m.Id));
        var detail = (await admin.GetFromJsonAsync<MovementDto>($"/api/v1/inventory/movements/{transfer.Id}"))!;
        Assert.Equal(transfer with { Lines = detail.Lines }, detail);
        Assert.Equal(transfer.Lines, detail.Lines);
        Assert.Equal(1, pages[0].Items[0].LineCount);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("both")]
    [InlineData("global")]
    public async Task test_movements_warehouse_scope_filters_totals_and_protects_full_transfers(string mode)
    {
        var data = await _data.SeedAsync();
        using var admin = await _data.LoginAsync();
        var entry = await OperationalApiData.MoveAsync(admin, data, "entries", 100);
        var destinationEntry = await OperationalApiData.MoveAsync(admin, data, "entries", 10, data.Destination);
        var transfer = await OperationalApiData.MoveAsync(admin, data, "transfers", 20);
        Guid[] warehouses = mode switch { "source" => [data.Source], "destination" => [data.Destination], "both" => [data.Source, data.Destination], _ => [] };
        var user = await _data.UserAsync([Permissions.Inventory.Read], warehouses, mode == "global");
        using var client = await _data.LoginAsync(user);
        var page = await OperationalApiData.PageAsync<MovementSummaryDto>(client, $"/api/v1/inventory/movements?productId={data.Product}");
        var fullAccess = mode is "both" or "global";
        var expected = fullAccess ? new[] { transfer.Id, destinationEntry.Id, entry.Id }
            : mode == "source" ? [entry.Id] : mode == "destination" ? [destinationEntry.Id] : [];
        Assert.Equal(expected.Length, page.TotalCount);
        Assert.Equal(expected, page.Items.Select(m => m.Id));
        var response = await client.GetAsync($"/api/v1/inventory/movements/{transfer.Id}");
        if (fullAccess) Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        else await OperationalApiData.ProblemAsync(response, HttpStatusCode.Forbidden);
        if (!fullAccess)
        {
            var denied = mode == "source" ? data.Destination : data.Source;
            await OperationalApiData.ProblemAsync(await client.GetAsync($"/api/v1/inventory/movements?warehouseId={denied}"), HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task test_operational_queries_anonymous_and_denied_permissions_return_problem_details()
    {
        var paths = new[] { "/api/v1/audit-logs", "/api/v1/inventory/movements", "/api/v2/inventory/stock", "/api/v2/inventory/kardex" };
        using var anonymous = fixture.Factory.CreateClient();
        var user = await _data.UserAsync([], [], true, RoleDefinitions.Administrator,
            [Permissions.Inventory.Read, Permissions.Audit.Read]);
        using var denied = await _data.LoginAsync(user);
        foreach (var path in paths)
        {
            await OperationalApiData.ProblemAsync(await anonymous.GetAsync(path), HttpStatusCode.Unauthorized);
            await OperationalApiData.ProblemAsync(await denied.GetAsync(path), HttpStatusCode.Forbidden);
        }
    }

    [Fact]
    public async Task test_movements_filters_combine_type_product_warehouse_and_dates()
    {
        var data = await _data.SeedAsync();
        using var admin = await _data.LoginAsync();
        await OperationalApiData.MoveAsync(admin, data, "entries", 100);
        var transfer = await OperationalApiData.MoveAsync(admin, data, "transfers", 20);
        var timestamp = Uri.EscapeDataString(transfer.CreatedAtUtc.ToString("O"));
        var path = $"/api/v1/inventory/movements?productId={data.Product}&warehouseId={data.Destination}&type=Transfer&fromUtc={timestamp}&toUtc={timestamp}";
        var page = await OperationalApiData.PageAsync<MovementSummaryDto>(admin, path);
        Assert.Equal(transfer.Id, Assert.Single(page.Items).Id);
        Assert.Equal(1, page.TotalCount);
        var empty = await OperationalApiData.PageAsync<MovementSummaryDto>(admin, path + "&page=2");
        Assert.Empty(empty.Items);
        Assert.Equal(1, empty.TotalCount);
        await OperationalApiData.ProblemAsync(await admin.GetAsync($"/api/v1/inventory/movements/{Guid.NewGuid()}"), HttpStatusCode.NotFound);
    }

    [Theory]
    [InlineData("/api/v1/inventory/movements?page=0")]
    [InlineData("/api/v1/inventory/movements?page=2147483647")]
    [InlineData("/api/v1/inventory/movements?type=999")]
    [InlineData("/api/v1/inventory/movements?fromUtc=2026-02-02T00:00:00Z&toUtc=2026-01-01T00:00:00Z")]
    [InlineData("/api/v1/audit-logs?pageSize=201")]
    [InlineData("/api/v1/audit-logs?action=Unknown")]
    [InlineData("/api/v1/audit-logs?fromUtc=2026-01-01T00:00:00")]
    [InlineData("/api/v2/inventory/stock?pageSize=0")]
    [InlineData("/api/v2/inventory/kardex")]
    public async Task test_operational_queries_invalid_filters_return_400(string path)
    {
        using var admin = await _data.LoginAsync();
        await OperationalApiData.ProblemAsync(await admin.GetAsync(path), HttpStatusCode.BadRequest);
    }
}
