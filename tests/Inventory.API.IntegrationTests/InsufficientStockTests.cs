using System.Net;
using System.Net.Http.Json;
using Inventory.Application.Features.Inventory;
using Inventory.Domain.Inventory;
using Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class InsufficientStockTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Theory]
    [InlineData("exits", "missing")]
    [InlineData("exits", "zero")]
    [InlineData("exits", "insufficient")]
    [InlineData("exits", "reserved")]
    [InlineData("transfers", "missing")]
    [InlineData("transfers", "zero")]
    [InlineData("transfers", "insufficient")]
    [InlineData("transfers", "reserved")]
    public async Task Unavailable_stock_has_one_422_error_without_partial_writes(string operation, string state)
    {
        var helper = new OperationalApiData(fixture.Factory);
        var data = await helper.SeedAsync();
        using var client = await helper.LoginAsync();
        await OperationalApiData.MoveAsync(client, data, "entries", 10, product: data.OtherProduct);
        if (state is "insufficient" or "reserved")
            await OperationalApiData.MoveAsync(client, data, "entries", state == "reserved" ? 3 : 1);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (state == "zero") db.Stocks.Add(new Stock(data.Product, data.Source));
        if (state == "reserved") (await db.Stocks.SingleAsync(stock => stock.ProductId == data.Product && stock.WarehouseId == data.Source)).Reserve(3);
        await db.SaveChangesAsync();
        var ids = new[] { data.Product, data.OtherProduct };
        var before = await db.Stocks.AsNoTracking().Where(stock => ids.Contains(stock.ProductId))
            .OrderBy(stock => stock.WarehouseId).ThenBy(stock => stock.ProductId)
            .Select(stock => new { stock.ProductId, stock.WarehouseId, stock.Quantity, stock.ReservedQuantity, stock.Version }).ToArrayAsync();
        var count = await db.InventoryMovements.CountAsync();
        var audit = await db.AuditLogs.CountAsync();
        MovementLineInput[] lines = [new(data.OtherProduct, 1), new(data.Product, 2)];
        object command = operation == "exits"
            ? new CreateExitCommand(data.Source, lines, IdempotencyKey: Guid.NewGuid())
            : new CreateTransferCommand(data.Source, data.Destination, lines, IdempotencyKey: Guid.NewGuid());
        var problem = await OperationalApiData.ProblemAsync(await client.PostAsJsonAsync("/api/v1/inventory/" + operation, command),
            HttpStatusCode.UnprocessableEntity);
        Assert.Equal("Insufficient available stock.", problem.Detail);
        Assert.DoesNotContain(data.Product.ToString(), problem.Detail!);
        Assert.DoesNotContain(data.Source.ToString(), problem.Detail!);
        Assert.Equal(before, await db.Stocks.AsNoTracking().Where(stock => ids.Contains(stock.ProductId))
            .OrderBy(stock => stock.WarehouseId).ThenBy(stock => stock.ProductId)
            .Select(stock => new { stock.ProductId, stock.WarehouseId, stock.Quantity, stock.ReservedQuantity, stock.Version }).ToArrayAsync());
        Assert.Equal(count, await db.InventoryMovements.CountAsync());
        Assert.Equal(audit, await db.AuditLogs.CountAsync());
    }
}
