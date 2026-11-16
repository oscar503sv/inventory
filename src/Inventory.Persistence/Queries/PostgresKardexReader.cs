using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Queries;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Common;
using Shared.Contracts.Inventory;

namespace Inventory.Persistence.Queries;

public sealed class PostgresKardexReader(AppDbContext db) : IKardexReader
{
    public async Task<PagedResult<KardexEntryDto>> ReadAsync(Guid product, Guid warehouse, DateTime? fromUtc, DateTime? toUtc,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var timestamps = from line in db.InventoryMovementLines.AsNoTracking()
                         join movement in db.InventoryMovements on line.MovementId equals movement.Id
                         where line.ProductId == product && (movement.WarehouseId == warehouse || movement.DestinationWarehouseId == warehouse)
                         select movement.CreatedAtUtc;
        if (fromUtc is { } from) timestamps = timestamps.Where(timestamp => timestamp >= from);
        if (toUtc is { } to) timestamps = timestamps.Where(timestamp => timestamp <= to);
        var total = await timestamps.CountAsync(cancellationToken);
        var offset = QueryPaging.Offset(page, pageSize);
        if (offset >= total) return new([], page, pageSize, total);
        var rows = Balances(product, warehouse);
        // These predicates are deliberately outside the windows: excluded rows still affect opening balances.
        if (fromUtc is { } lower) rows = rows.Where(row => row.TimestampUtc >= lower);
        if (toUtc is { } upper) rows = rows.Where(row => row.TimestampUtc <= upper);
        var items = await rows.OrderBy(row => row.Sequence).Skip(offset).Take(pageSize)
            .ToListAsync(cancellationToken);
        return new(items.Select(row => new KardexEntryDto(row.TimestampUtc, row.Number, row.Type,
            warehouse, row.Quantity, row.RunningBalance)).ToList(), page, pageSize, total);
    }

    private IQueryable<KardexBalanceRow> Balances(Guid product, Guid warehouse)
        => db.Database.SqlQuery<KardexBalanceRow>($"""
            WITH history AS (
                SELECT m."LedgerSequence" AS "Sequence", m."MovementNumber" AS "Number",
                       m."MovementType" AS "Type", m."CreatedAtUtc" AS "TimestampUtc", l."Quantity" AS "AbsoluteQuantity",
                       CASE m."MovementType"
                           WHEN 'Entry' THEN l."Quantity"
                           WHEN 'Exit' THEN -l."Quantity"
                           WHEN 'Transfer' THEN CASE WHEN m."WarehouseId" = {warehouse} THEN -l."Quantity" ELSE l."Quantity" END
                           ELSE 0::numeric END AS "Delta"
                FROM inventory_movement_lines l JOIN inventory_movements m ON l."MovementId" = m."Id"
                WHERE l."ProductId" = {product} AND (m."WarehouseId" = {warehouse} OR m."DestinationWarehouseId" = {warehouse})
            ), segments AS (
                SELECT *, SUM(CASE WHEN "Type" = 'Adjustment' THEN 1 ELSE 0 END)
                    OVER (ORDER BY "Sequence" ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS "Segment"
                FROM history
            ), balances AS (
                SELECT *, SUM(CASE WHEN "Type" = 'Adjustment' THEN "AbsoluteQuantity" ELSE "Delta" END)
                    OVER (PARTITION BY "Segment" ORDER BY "Sequence" ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW) AS "RunningBalance"
                FROM segments
            )
            SELECT "Sequence", "Number", "Type", "TimestampUtc", "RunningBalance",
                   CASE WHEN "Type" = 'Adjustment'
                        THEN "RunningBalance" - COALESCE(LAG("RunningBalance") OVER (ORDER BY "Sequence"), 0::numeric)
                        ELSE "Delta" END AS "Quantity"
            FROM balances
            """);
}

public sealed class KardexBalanceRow
{
    public long Sequence { get; set; }
    public string Number { get; set; } = "";
    public string Type { get; set; } = "";
    public DateTime TimestampUtc { get; set; }
    public decimal Quantity { get; set; }
    public decimal RunningBalance { get; set; }
}
