using System.Net;
using System.Net.Http.Json;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Features.Inventory;
using Inventory.Domain.Inventory;
using Inventory.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class MovementFailureBoundaryTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Accumulated_stock_overflow_returns_422_and_rolls_back_the_whole_movement(bool transfer)
    {
        var helper = new OperationalApiData(fixture.Factory);
        var data = await helper.SeedAsync();
        using var client = await helper.LoginAsync();
        await OperationalApiData.MoveAsync(client, data, "entries", transfer ? 20 : InventoryNumericLimits.MaxStockQuantity);
        if (transfer) await OperationalApiData.MoveAsync(client, data, "entries", InventoryNumericLimits.MaxStockQuantity, data.Destination);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var before = await db.Stocks.AsNoTracking().Where(stock => stock.ProductId == data.Product)
            .OrderBy(stock => stock.WarehouseId).Select(stock => stock.Quantity).ToArrayAsync();
        var movements = await db.InventoryMovements.CountAsync();
        var audit = await db.AuditLogs.CountAsync();
        MovementLineInput[] lines = [new(data.Product, 1)];
        object command = transfer
            ? new CreateTransferCommand(data.Source, data.Destination, lines, IdempotencyKey: Guid.NewGuid())
            : new CreateEntryCommand(data.Source, lines, IdempotencyKey: Guid.NewGuid());
        await OperationalApiData.ProblemAsync(await client.PostAsJsonAsync("/api/v1/inventory/" + (transfer ? "transfers" : "entries"), command),
            HttpStatusCode.UnprocessableEntity);
        Assert.Equal(before, await db.Stocks.AsNoTracking().Where(stock => stock.ProductId == data.Product)
            .OrderBy(stock => stock.WarehouseId).Select(stock => stock.Quantity).ToArrayAsync());
        Assert.Equal(movements, await db.InventoryMovements.CountAsync());
        Assert.Equal(audit, await db.AuditLogs.CountAsync());
    }

    [Fact]
    public async Task Movement_number_collision_is_a_409_without_partial_stock_or_audit()
    {
        var number = "COLL-" + Guid.NewGuid().ToString("N")[..12];
        using var factory = fixture.Factory.WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.AddScoped<IMovementNumberGenerator>(_ => new FixedNumber(number))));
        var helper = new OperationalApiData(factory);
        var data = await helper.SeedAsync();
        using var client = await helper.LoginAsync();
        await OperationalApiData.MoveAsync(client, data, "entries", 1);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var audit = await db.AuditLogs.CountAsync();
        await OperationalApiData.ProblemAsync(await client.PostAsJsonAsync("/api/v1/inventory/entries",
            new CreateEntryCommand(data.Source, [new(data.Product, 1)], IdempotencyKey: Guid.NewGuid())), HttpStatusCode.Conflict);
        Assert.Equal(1, await db.InventoryMovements.CountAsync(movement => movement.MovementNumber == number));
        Assert.Equal(1, await db.Stocks.Where(stock => stock.ProductId == data.Product && stock.WarehouseId == data.Source)
            .Select(stock => stock.Quantity).SingleAsync());
        Assert.Equal(audit, await db.AuditLogs.CountAsync());
    }

    private sealed class FixedNumber(string number) : IMovementNumberGenerator
    {
        public Task<string> NextAsync(MovementType type, CancellationToken cancellationToken = default) => Task.FromResult(number);
    }
}
