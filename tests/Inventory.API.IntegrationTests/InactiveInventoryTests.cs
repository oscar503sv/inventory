using System.Net;
using System.Net.Http.Json;
using Inventory.Application.Features.Inventory;
using Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shared.Contracts.Inventory;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class InactiveInventoryTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Theory]
    [InlineData("entries", "product")]
    [InlineData("exits", "product")]
    [InlineData("adjustments", "product")]
    [InlineData("transfers", "product")]
    [InlineData("entries", "source")]
    [InlineData("exits", "source")]
    [InlineData("adjustments", "source")]
    [InlineData("transfers", "source")]
    [InlineData("transfers", "destination")]
    public async Task Inactive_entities_reject_new_movements_without_mutating_stock_or_audit(string operation, string inactive)
    {
        var setup = new OperationalApiData(fixture.Factory);
        var data = await setup.SeedAsync();
        using var client = await setup.LoginAsync();
        await OperationalApiData.MoveAsync(client, data, "entries", 20);
        await OperationalApiData.MoveAsync(client, data, "entries", 10, warehouse: data.Destination);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (inactive == "product") (await db.Products.SingleAsync(p => p.Id == data.Product)).Deactivate();
        else (await db.Warehouses.SingleAsync(w => w.Id == (inactive == "source" ? data.Source : data.Destination))).Deactivate();
        await db.SaveChangesAsync();
        var before = await db.Stocks.AsNoTracking().Where(s => s.ProductId == data.Product)
            .OrderBy(s => s.WarehouseId).Select(s => s.Quantity).ToArrayAsync();
        var movements = await db.InventoryMovements.CountAsync();
        var audit = await db.AuditLogs.CountAsync();
        await OperationalApiData.ProblemAsync(await client.PostAsJsonAsync("/api/v1/inventory/" + operation,
            Command(operation, data, Guid.NewGuid())), HttpStatusCode.UnprocessableEntity);
        Assert.Equal(before, await db.Stocks.AsNoTracking().Where(s => s.ProductId == data.Product)
            .OrderBy(s => s.WarehouseId).Select(s => s.Quantity).ToArrayAsync());
        Assert.Equal(movements, await db.InventoryMovements.CountAsync());
        Assert.Equal(audit, await db.AuditLogs.CountAsync());
    }

    [Theory]
    [InlineData("entries", 22)]
    [InlineData("exits", 18)]
    [InlineData("adjustments", 2)]
    [InlineData("transfers", 18)]
    public async Task Active_movements_keep_stock_semantics_and_replay_after_deactivation(string operation, int expected)
    {
        var setup = new OperationalApiData(fixture.Factory);
        var data = await setup.SeedAsync();
        using var client = await setup.LoginAsync();
        await OperationalApiData.MoveAsync(client, data, "entries", 20);
        var command = Command(operation, data, Guid.NewGuid());
        using var response = await client.PostAsJsonAsync("/api/v1/inventory/" + operation, command);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var movement = (await response.Content.ReadFromJsonAsync<MovementDto>())!;
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(expected, await db.Stocks.AsNoTracking().Where(s => s.ProductId == data.Product && s.WarehouseId == data.Source)
            .Select(s => s.Quantity).SingleAsync());
        if (operation == "transfers")
            Assert.Equal(2, await db.Stocks.Where(s => s.ProductId == data.Product && s.WarehouseId == data.Destination)
                .Select(s => s.Quantity).SingleAsync());
        (await db.Products.SingleAsync(p => p.Id == data.Product)).Deactivate();
        (await db.Warehouses.SingleAsync(w => w.Id == data.Source)).Deactivate();
        await db.SaveChangesAsync();
        var count = await db.InventoryMovements.CountAsync();
        using var replay = await client.PostAsJsonAsync("/api/v1/inventory/" + operation, command);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(movement.Id, (await replay.Content.ReadFromJsonAsync<MovementDto>())!.Id);
        Assert.Equal(count, await db.InventoryMovements.CountAsync());
    }

    [Theory]
    [InlineData("entries", true)]
    [InlineData("exits", true)]
    [InlineData("adjustments", true)]
    [InlineData("transfers", true)]
    [InlineData("entries", false)]
    [InlineData("exits", false)]
    [InlineData("adjustments", false)]
    [InlineData("transfers", false)]
    public async Task Missing_resources_remain_not_found(string operation, bool product)
    {
        var setup = new OperationalApiData(fixture.Factory);
        var data = await setup.SeedAsync();
        using var client = await setup.LoginAsync();
        data = product ? data with { Product = Guid.NewGuid() } : data with { Source = Guid.NewGuid() };
        await OperationalApiData.ProblemAsync(await client.PostAsJsonAsync("/api/v1/inventory/" + operation,
            Command(operation, data, Guid.NewGuid())), HttpStatusCode.NotFound);
    }

    private static object Command(string operation, InventoryScenario data, Guid key)
    {
        MovementLineInput[] lines = [new(data.Product, 2, 1)];
        return operation switch
        {
            "entries" => new CreateEntryCommand(data.Source, lines, "Entry", key),
            "exits" => new CreateExitCommand(data.Source, lines, "Exit", key),
            "adjustments" => new CreateAdjustmentCommand(data.Source, lines, "Physical count", key),
            "transfers" => new CreateTransferCommand(data.Source, data.Destination, lines, "Transfer", key),
            _ => throw new ArgumentOutOfRangeException(nameof(operation))
        };
    }
}
