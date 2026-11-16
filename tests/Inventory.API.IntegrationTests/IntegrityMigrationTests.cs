using Inventory.Domain.Inventory;
using Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class IntegrityMigrationTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Fact]
    public async Task Upgrade_preserves_legacy_order_and_removes_previously_audited_credentials()
    {
        var data = await new OperationalApiData(fixture.Factory).SeedAsync();
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260609001034_InitialCreate");
        var actor = await db.Users.Select(u => u.Id).SingleAsync();
        var product = data.Product;
        var warehouse = data.Source;
        var older = DateTime.UtcNow.AddDays(-2);
        var newer = older.AddDays(1);
        // Insert the later movement first: physical row order must not determine the backfill.
        foreach (var (number, type, time, quantity) in new[]
        {
            ("LEGACY-2", "Adjustment", newer, 5m), ("LEGACY-1", "Entry", older, 10m)
        })
        {
            var id = Guid.NewGuid();
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO inventory_movements
                    ("Id", "MovementNumber", "MovementType", "WarehouseId", "Status", "CreatedBy", "CreatedAtUtc", "Notes")
                VALUES ({id}, {number}, {type}, {warehouse}, 'Confirmed', {actor}, {time}, 'Legacy');
                """);
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO inventory_movement_lines ("Id", "MovementId", "ProductId", "Quantity", "UnitCost")
                VALUES ({Guid.NewGuid()}, {id}, {product}, {quantity}, 0);
                """);
        }
        var auditId = Guid.NewGuid();
        var stockId = Guid.NewGuid();
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO stocks ("Id", "ProductId", "WarehouseId", "Quantity", "ReservedQuantity")
            VALUES ({stockId}, {product}, {warehouse}, 5, 2);
            """);
        const string sensitive = "{\"Email\":\"legacy@example.com\",\"PasswordHash\":\"unsafe-hash\",\"Password\":\"unsafe-password\",\"TokenHash\":\"unsafe-token\"}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO audit_logs ("Id", "TimestampUtc", "Entity", "Action", "OldValues", "NewValues")
            VALUES ({auditId}, {older}, 'User', 'Updated', CAST({sensitive} AS jsonb), CAST({sensitive} AS jsonb));
            """);
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var movements = await db.InventoryMovements.AsNoTracking().Where(m => m.WarehouseId == warehouse).OrderBy(m => m.LedgerSequence).ToListAsync();
        Assert.Equal(new[] { "LEGACY-1", "LEGACY-2" }, movements.Select(m => m.MovementNumber));
        Assert.All(movements, m => Assert.Null(m.IdempotencyKey));
        var stock = await db.Stocks.AsNoTracking().SingleAsync(s => s.Id == stockId);
        Assert.Equal(5m, stock.Quantity);
        Assert.Equal(2m, stock.ReservedQuantity);
        var audit = await db.AuditLogs.AsNoTracking().SingleAsync(a => a.Id == auditId);
        foreach (var json in new[] { audit.OldValues!, audit.NewValues! })
        {
            Assert.DoesNotContain("unsafe", json);
            Assert.DoesNotContain("Hash", json);
            Assert.DoesNotContain("Password", json);
            Assert.Contains("legacy@example.com", json);
        }
        var next = InventoryMovement.CreateEntry("NEW-3", warehouse, actor, DateTime.UtcNow,
            [new InventoryMovementLine(product, 2, 0)]);
        db.InventoryMovements.Add(next);
        await db.SaveChangesAsync();
        Assert.True(next.LedgerSequence > movements.Last().LedgerSequence);
    }
    [Theory]
    [InlineData("stocks")]
    [InlineData("movements")]
    [InlineData("lines")]
    public async Task Upgrade_rejects_invalid_legacy_data_without_changing_it(string table)
    {
        var helper = new OperationalApiData(fixture.Factory);
        var data = await helper.SeedAsync();
        using var client = await helper.LoginAsync();
        var movement = await OperationalApiData.MoveAsync(client, data, "entries", 10);
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261113035606_InventoryIntegrity");
        var missing = Guid.NewGuid();
        FormattableString corrupt = table switch
        {
            "stocks" => $"UPDATE stocks SET \"Quantity\" = -1 WHERE \"ProductId\" = {data.Product}",
            "movements" => $"UPDATE inventory_movements SET \"DestinationWarehouseId\" = {missing} WHERE \"Id\" = {movement.Id}",
            "lines" => $"UPDATE inventory_movement_lines SET \"ProductId\" = {missing} WHERE \"MovementId\" = {movement.Id}",
            _ => throw new ArgumentOutOfRangeException(nameof(table))
        };
        await db.Database.ExecuteSqlInterpolatedAsync(corrupt);
        try
        {
            var error = await Assert.ThrowsAsync<PostgresException>(() => migrator.MigrateAsync());
            Assert.Equal(PostgresErrorCodes.RaiseException, error.SqlState);
            Assert.Contains("Inventory integrity precheck failed", error.MessageText);
            Assert.DoesNotContain("20261116052329_InventoryRelationalConstraints", await db.Database.GetAppliedMigrationsAsync());
            Assert.Contains("20261116052329_InventoryRelationalConstraints", await db.Database.GetPendingMigrationsAsync());
            if (table == "stocks")
                Assert.Equal(-1m, await db.Stocks.Where(s => s.ProductId == data.Product).Select(s => s.Quantity).SingleAsync());
            else if (table == "movements")
                Assert.Equal(missing, await db.InventoryMovements.Where(m => m.Id == movement.Id).Select(m => m.DestinationWarehouseId).SingleAsync());
            else
                Assert.Equal(missing, await db.InventoryMovementLines.Where(l => l.MovementId == movement.Id).Select(l => l.ProductId).SingleAsync());
        }
        finally
        {
            // Restore only this test's deliberately invalid row before verifying a successful retry.
            FormattableString repair = table switch
            {
                "stocks" => $"UPDATE stocks SET \"Quantity\" = 10 WHERE \"ProductId\" = {data.Product}",
                "movements" => $"UPDATE inventory_movements SET \"DestinationWarehouseId\" = NULL WHERE \"Id\" = {movement.Id}",
                "lines" => $"UPDATE inventory_movement_lines SET \"ProductId\" = {data.Product} WHERE \"MovementId\" = {movement.Id}",
                _ => throw new ArgumentOutOfRangeException(nameof(table))
            };
            await db.Database.ExecuteSqlInterpolatedAsync(repair);
            await migrator.MigrateAsync();
        }
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

}
