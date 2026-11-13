using Inventory.Domain.Inventory;
using Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Inventory.API.IntegrationTests;

[Collection("Postgres API")]
public sealed class IntegrityMigrationTests(PostgresApiFixture fixture) : IClassFixture<PostgresApiFixture>
{
    [Fact]
    public async Task Upgrade_preserves_legacy_order_and_removes_previously_audited_credentials()
    {
        using var scope = fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20260609001034_InitialCreate");
        var actor = await db.Users.Select(u => u.Id).SingleAsync();
        var product = Guid.NewGuid();
        var warehouse = Guid.NewGuid();
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
        const string sensitive = "{\"Email\":\"legacy@example.com\",\"PasswordHash\":\"unsafe-hash\",\"Password\":\"unsafe-password\",\"TokenHash\":\"unsafe-token\"}";
        await db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO audit_logs ("Id", "TimestampUtc", "Entity", "Action", "OldValues", "NewValues")
            VALUES ({auditId}, {older}, 'User', 'Updated', CAST({sensitive} AS jsonb), CAST({sensitive} AS jsonb));
            """);
        await migrator.MigrateAsync();
        Assert.False(db.Database.HasPendingModelChanges());
        var movements = await db.InventoryMovements.AsNoTracking().OrderBy(m => m.LedgerSequence).ToListAsync();
        Assert.Equal(new[] { "LEGACY-1", "LEGACY-2" }, movements.Select(m => m.MovementNumber));
        Assert.All(movements, m => Assert.Null(m.IdempotencyKey));
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
}
