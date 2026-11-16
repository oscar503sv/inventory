using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Queries;
using Inventory.Domain.Inventory;
using Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Common;
using Shared.Contracts.Inventory;

namespace Inventory.Application.Tests.Integration;

// The lightweight movement-flow tests use a reader double; PostgreSQL window semantics are tested by the API suite.
internal sealed class TestKardexReader(AppDbContext db) : IKardexReader
{
    public async Task<PagedResult<KardexEntryDto>> ReadAsync(Guid product, Guid warehouse, DateTime? fromUtc, DateTime? toUtc,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var rows = await (from line in db.InventoryMovementLines.AsNoTracking()
                          join movement in db.InventoryMovements on line.MovementId equals movement.Id
                          where line.ProductId == product && (movement.WarehouseId == warehouse || movement.DestinationWarehouseId == warehouse)
                          orderby movement.LedgerSequence
                          select new
                          {
                              movement.LedgerSequence,
                              movement.MovementNumber,
                              movement.MovementType,
                              movement.CreatedAtUtc,
                              movement.WarehouseId,
                              line.Quantity
                          }).ToListAsync(cancellationToken);
        var result = new List<KardexEntryDto>();
        decimal balance = 0;
        foreach (var row in rows)
        {
            var opening = balance;
            balance = row.MovementType switch
            {
                MovementType.Adjustment => row.Quantity,
                MovementType.Entry => balance + row.Quantity,
                MovementType.Exit => balance - row.Quantity,
                MovementType.Transfer => row.WarehouseId == warehouse ? balance - row.Quantity : balance + row.Quantity,
                _ => throw new ArgumentOutOfRangeException()
            };
            if ((fromUtc is null || row.CreatedAtUtc >= fromUtc) && (toUtc is null || row.CreatedAtUtc <= toUtc))
                result.Add(new(row.CreatedAtUtc, row.MovementNumber, row.MovementType.ToString(), warehouse, balance - opening, balance));
        }
        return new(result.Skip(QueryPaging.Offset(page, pageSize)).Take(pageSize).ToList(), page, pageSize, result.Count);
    }
}
