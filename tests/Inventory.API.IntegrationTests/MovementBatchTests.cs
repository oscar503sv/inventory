using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using Inventory.Application.Features.Inventory;
using Inventory.Domain.Catalog;
using Inventory.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Inventory;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class MovementBatchTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Theory]
    [InlineData("entries", 25)]
    [InlineData("exits", 25)]
    [InlineData("adjustments", 25)]
    [InlineData("transfers", 25)]
    [InlineData("entries", 500)]
    [InlineData("transfers", 500)]
    public async Task Whole_movement_uses_one_stock_query_and_creates_missing_stocks_in_memory(string operation, int count)
    {
        var counter = new MovementStockQueryCounter();
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddDbContext<AppDbContext>(options => options.AddInterceptors(counter))));
        var helper = new OperationalApiData(factory);
        var data = await helper.SeedAsync();
        using var client = await helper.LoginAsync();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var template = await db.Products.SingleAsync(product => product.Id == data.Product);
        var products = Enumerable.Range(0, count).Select(_ => Product.Create("B" + Guid.NewGuid().ToString("N")[..16],
            "P", "Batch Product", "", template.CategoryId, template.BrandId, template.UnitOfMeasureId, 0)).ToArray();
        db.Products.AddRange(products);
        await db.SaveChangesAsync();
        var lines = products.Select(product => new MovementLineInput(product.Id, 2, 0)).ToArray();
        if (operation != "entries")
        {
            using var seed = await client.PostAsJsonAsync("/api/v1/inventory/entries", new CreateEntryCommand(data.Source,
                products.Select(product => new MovementLineInput(product.Id, 10)).ToArray(), IdempotencyKey: Guid.NewGuid()));
            Assert.Equal(HttpStatusCode.Created, seed.StatusCode);
        }
        counter.Reset();
        using var response = await client.PostAsJsonAsync("/api/v1/inventory/" + operation,
            Command(operation, data, lines, new string('n', 1024)));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(count, (await response.Content.ReadFromJsonAsync<MovementDto>())!.Lines.Count);
        Assert.Equal(1, counter.Count);
        var ids = products.Select(product => product.Id).ToArray();
        var rows = await db.Stocks.AsNoTracking().Where(stock => ids.Contains(stock.ProductId)).ToArrayAsync();
        Assert.Equal(operation == "transfers" ? count * 2 : count, rows.Length);
        Assert.All(rows.Where(stock => stock.WarehouseId == data.Source), stock =>
            Assert.Equal(operation is "entries" or "adjustments" ? 2 : 8, stock.Quantity));
        if (operation == "transfers") Assert.All(rows.Where(stock => stock.WarehouseId == data.Destination), stock => Assert.Equal(2, stock.Quantity));
    }

    [Theory]
    [InlineData("entries", true)]
    [InlineData("exits", true)]
    [InlineData("transfers", true)]
    [InlineData("adjustments", true)]
    [InlineData("entries", false)]
    [InlineData("exits", false)]
    [InlineData("transfers", false)]
    [InlineData("adjustments", false)]
    public async Task Excessive_lines_or_text_fail_validation_without_writes(string operation, bool linesTooLong)
    {
        var helper = new OperationalApiData(fixture.Factory);
        var data = await helper.SeedAsync();
        using var client = await helper.LoginAsync();
        var lines = Enumerable.Range(0, linesTooLong ? 501 : 1).Select(_ => new MovementLineInput(Guid.NewGuid(), 1)).ToArray();
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var movements = await db.InventoryMovements.CountAsync();
        var audit = await db.AuditLogs.CountAsync();
        await OperationalApiData.ProblemAsync(await client.PostAsJsonAsync("/api/v1/inventory/" + operation,
            Command(operation, data, lines, new string('n', linesTooLong ? 10 : 1025))), HttpStatusCode.BadRequest);
        Assert.Equal(movements, await db.InventoryMovements.CountAsync());
        Assert.Equal(audit, await db.AuditLogs.CountAsync());
    }

    private static object Command(string operation, InventoryScenario data, MovementLineInput[] lines, string text)
        => operation switch
        {
            "entries" => new CreateEntryCommand(data.Source, lines, text, Guid.NewGuid()),
            "exits" => new CreateExitCommand(data.Source, lines, text, Guid.NewGuid()),
            "adjustments" => new CreateAdjustmentCommand(data.Source, lines, text, Guid.NewGuid()),
            "transfers" => new CreateTransferCommand(data.Source, data.Destination, lines, text, Guid.NewGuid()),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
}

public sealed class MovementStockQueryCounter : DbCommandInterceptor
{
    private int _count;
    public int Count => Volatile.Read(ref _count);
    public void Reset() => Interlocked.Exchange(ref _count, 0);
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("Inventory.MovementStocks", StringComparison.Ordinal)) Interlocked.Increment(ref _count);
        return ValueTask.FromResult(result);
    }
}
