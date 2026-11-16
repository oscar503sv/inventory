using Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class InventoryConstraintTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Theory]
    [InlineData("stock-product", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("stock-warehouse", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("movement-source", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("movement-destination", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("line-product", PostgresErrorCodes.ForeignKeyViolation)]
    [InlineData("stock-negative", PostgresErrorCodes.CheckViolation)]
    [InlineData("reserved-negative", PostgresErrorCodes.CheckViolation)]
    [InlineData("reserved-excess", PostgresErrorCodes.CheckViolation)]
    [InlineData("line-zero", PostgresErrorCodes.CheckViolation)]
    [InlineData("line-negative", PostgresErrorCodes.CheckViolation)]
    [InlineData("cost-negative", PostgresErrorCodes.CheckViolation)]
    public async Task Database_rejects_invalid_inventory_even_when_application_validation_is_bypassed(string rule, string sqlState)
    {
        var helper = new OperationalApiData(fixture.Factory);
        var data = await helper.SeedAsync();
        using var client = await helper.LoginAsync();
        var movement = await OperationalApiData.MoveAsync(client, data, "entries", 10);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var missing = Guid.NewGuid();
        FormattableString sql = rule switch
        {
            "stock-product" => $"UPDATE stocks SET \"ProductId\" = {missing} WHERE \"ProductId\" = {data.Product}",
            "stock-warehouse" => $"UPDATE stocks SET \"WarehouseId\" = {missing} WHERE \"ProductId\" = {data.Product}",
            "movement-source" => $"UPDATE inventory_movements SET \"WarehouseId\" = {missing} WHERE \"Id\" = {movement.Id}",
            "movement-destination" => $"UPDATE inventory_movements SET \"DestinationWarehouseId\" = {missing} WHERE \"Id\" = {movement.Id}",
            "line-product" => $"UPDATE inventory_movement_lines SET \"ProductId\" = {missing} WHERE \"MovementId\" = {movement.Id}",
            "stock-negative" => $"UPDATE stocks SET \"Quantity\" = -1 WHERE \"ProductId\" = {data.Product}",
            "reserved-negative" => $"UPDATE stocks SET \"ReservedQuantity\" = -1 WHERE \"ProductId\" = {data.Product}",
            "reserved-excess" => $"UPDATE stocks SET \"ReservedQuantity\" = 11 WHERE \"ProductId\" = {data.Product}",
            "line-zero" => $"UPDATE inventory_movement_lines SET \"Quantity\" = 0 WHERE \"MovementId\" = {movement.Id}",
            "line-negative" => $"UPDATE inventory_movement_lines SET \"Quantity\" = -1 WHERE \"MovementId\" = {movement.Id}",
            "cost-negative" => $"UPDATE inventory_movement_lines SET \"UnitCost\" = -1 WHERE \"MovementId\" = {movement.Id}",
            _ => throw new ArgumentOutOfRangeException(nameof(rule))
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(sql));
        Assert.Equal(sqlState, error.SqlState);
        var stock = await db.Stocks.AsNoTracking().SingleAsync(s => s.ProductId == data.Product);
        Assert.Equal(data.Source, stock.WarehouseId);
        Assert.Equal(10m, stock.Quantity);
        Assert.Equal(0m, stock.ReservedQuantity);
        var stored = await db.InventoryMovements.AsNoTracking().Include(m => m.Lines).SingleAsync(m => m.Id == movement.Id);
        Assert.Equal(data.Source, stored.WarehouseId);
        Assert.Null(stored.DestinationWarehouseId);
        var line = Assert.Single(stored.Lines);
        Assert.Equal(data.Product, line.ProductId);
        Assert.Equal(10m, line.Quantity);
        Assert.Equal(2m, line.UnitCost);
    }

    [Theory]
    [InlineData("product")]
    [InlineData("source")]
    [InlineData("destination")]
    [InlineData("movement")]
    public async Task Hard_delete_cannot_orphan_inventory_or_cascade_movement_lines(string target)
    {
        var helper = new OperationalApiData(fixture.Factory);
        var data = await helper.SeedAsync();
        using var client = await helper.LoginAsync();
        await OperationalApiData.MoveAsync(client, data, "entries", 10);
        var transfer = await OperationalApiData.MoveAsync(client, data, "transfers", 2);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        FormattableString sql = target switch
        {
            "product" => $"DELETE FROM products WHERE \"Id\" = {data.Product}",
            "source" => $"DELETE FROM warehouses WHERE \"Id\" = {data.Source}",
            "destination" => $"DELETE FROM warehouses WHERE \"Id\" = {data.Destination}",
            "movement" => $"DELETE FROM inventory_movements WHERE \"Id\" = {transfer.Id}",
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        var error = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(sql));
        Assert.Equal(PostgresErrorCodes.RestrictViolation, error.SqlState);
        Assert.True(await db.InventoryMovements.AnyAsync(m => m.Id == transfer.Id));
        Assert.Single(await db.InventoryMovementLines.Where(l => l.MovementId == transfer.Id).ToListAsync());
        Assert.Equal(2, await db.Stocks.CountAsync(s => s.ProductId == data.Product));
    }
}
