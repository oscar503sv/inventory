using System.Data.Common;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Domain.Inventory;
using Inventory.Persistence;
using Inventory.Persistence.Seed;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Shared.Contracts.Common;
using Shared.Contracts.Inventory;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class KardexQueryPlanTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Fact]
    public async Task Capture_explain_for_a_500_movement_history_and_200_row_page()
    {
        var capture = new KardexSqlCapture();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(capture))));
        var helper = new OperationalApiData(factory);
        var data = await helper.SeedAsync();
        using var client = await helper.LoginAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var actor = await db.Users.Where(u => u.Email == DatabaseSeeder.DefaultAdminEmail).Select(u => u.Id).SingleAsync();
        var prefix = Guid.NewGuid().ToString("N")[..8];
        var timestamp = DateTime.UtcNow;
        for (var i = 0; i < 500; i++)
        {
            var movement = InventoryMovement.CreateEntry($"PLAN-{prefix}-{i:D4}", data.Source, actor,
                timestamp.AddTicks(i * 10), [new InventoryMovementLine(data.Product, 1, 0)]);
            movement.SetIdempotency(Guid.NewGuid(), new string('A', 64));
            db.InventoryMovements.Add(movement);
        }
        var stock = new Stock(data.Product, data.Source);
        stock.Increase(500);
        db.Stocks.Add(stock);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("ANALYZE inventory_movements; ANALYZE inventory_movement_lines");
        capture.Enabled = true;
        var page = (await client.GetFromJsonAsync<PagedResult<KardexEntryDto>>(
            $"/api/v2/inventory/kardex?productId={data.Product}&warehouseId={data.Source}&pageSize=200"))!;
        capture.Enabled = false;
        Assert.Equal(500, page.TotalCount);
        Assert.Equal(200, page.Items.Count);
        Assert.Equal(200m, page.Items[^1].RunningBalance);
        Assert.NotNull(capture.Sql);
        await using var connection = new NpgsqlConnection(db.Database.GetConnectionString());
        await connection.OpenAsync();
        await using var explain = new NpgsqlCommand("EXPLAIN (ANALYZE, BUFFERS, FORMAT JSON) " + capture.Sql, connection);
        foreach (var parameter in capture.Parameters) explain.Parameters.Add(parameter);
        var plan = (string)(await explain.ExecuteScalarAsync())!;
        using var json = JsonDocument.Parse(plan);
        Assert.Equal(200d, json.RootElement[0].GetProperty("Plan").GetProperty("Actual Rows").GetDouble());
        var output = Environment.GetEnvironmentVariable("KARDEX_PLAN_OUTPUT");
        if (!string.IsNullOrWhiteSpace(output)) await File.WriteAllTextAsync(output, plan);
        var future = Uri.EscapeDataString(DateTime.UtcNow.AddYears(1).ToString("O"));
        foreach (var suffix in new[] { "&page=4", "&fromUtc=" + future })
        {
            capture.Clear();
            capture.Enabled = true;
            var empty = (await client.GetFromJsonAsync<PagedResult<KardexEntryDto>>(
                $"/api/v2/inventory/kardex?productId={data.Product}&warehouseId={data.Source}&pageSize=200{suffix}"))!;
            capture.Enabled = false;
            Assert.Empty(empty.Items);
            Assert.Equal(suffix == "&page=4" ? 500 : 0, empty.TotalCount);
            Assert.DoesNotContain("WITH history", capture.Sql ?? "", StringComparison.Ordinal);
        }
    }
}

public sealed class KardexSqlCapture : DbCommandInterceptor
{
    public bool Enabled { get; set; }
    public string? Sql { get; private set; }
    public NpgsqlParameter[] Parameters { get; private set; } = [];
    public void Clear()
    {
        Sql = null;
        Parameters = [];
    }
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (Enabled && command.CommandText.Contains("inventory_movement_lines", StringComparison.Ordinal))
        {
            Sql = command.CommandText;
            Parameters = command.Parameters.Cast<NpgsqlParameter>().Select(p => (NpgsqlParameter)((ICloneable)p).Clone()).ToArray();
        }
        return ValueTask.FromResult(result);
    }
}
