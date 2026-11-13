using Inventory.Application.Common.Abstractions;
using Inventory.Domain.Inventory;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Inventory;

namespace Inventory.Application.Features.Inventory;

// ----- Stock query -----

public sealed record GetStockQuery(Guid? ProductId = null, Guid? WarehouseId = null) : IRequest<IReadOnlyList<StockDto>>;

public sealed class GetStockQueryHandler : IRequestHandler<GetStockQuery, IReadOnlyList<StockDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IPermissionChecker _permissions;
    public GetStockQueryHandler(IApplicationDbContext db, IPermissionChecker permissions)
        => (_db, _permissions) = (db, permissions);

    public async Task<IReadOnlyList<StockDto>> Handle(GetStockQuery request, CancellationToken cancellationToken)
    {
        var query = _db.Stocks.AsNoTracking();
        if (request.WarehouseId is { } requestedWarehouse)
            await _permissions.EnsureWarehouseAccessAsync(requestedWarehouse, cancellationToken);
        var access = await _permissions.GetCurrentUserAuthorizationAsync(cancellationToken);
        if (!access.HasAllWarehousesAccess)
            query = query.Where(s => access.WarehouseScopes.Contains(s.WarehouseId));
        if (request.ProductId is { } productId)
            query = query.Where(s => s.ProductId == productId);
        if (request.WarehouseId is { } warehouseId)
            query = query.Where(s => s.WarehouseId == warehouseId);

        return await query
            .Select(s => new StockDto(
                s.ProductId, s.WarehouseId, s.Quantity, s.ReservedQuantity, s.Quantity - s.ReservedQuantity))
            .ToListAsync(cancellationToken);
    }
}

// ----- Kardex (stock ledger) query -----

public sealed record GetKardexQuery(
    Guid ProductId,
    Guid WarehouseId,
    DateTime? FromUtc = null,
    DateTime? ToUtc = null) : IRequest<IReadOnlyList<KardexEntryDto>>;

public sealed class GetKardexQueryHandler : IRequestHandler<GetKardexQuery, IReadOnlyList<KardexEntryDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly IPermissionChecker _permissions;
    public GetKardexQueryHandler(IApplicationDbContext db, IPermissionChecker permissions)
        => (_db, _permissions) = (db, permissions);

    public async Task<IReadOnlyList<KardexEntryDto>> Handle(GetKardexQuery request, CancellationToken cancellationToken)
    {
        var w = request.WarehouseId;
        await _permissions.EnsureWarehouseAccessAsync(w, cancellationToken);

        var rows = await (
            from line in _db.InventoryMovementLines
            join movement in _db.InventoryMovements on line.MovementId equals movement.Id
            where line.ProductId == request.ProductId
                  && (movement.WarehouseId == w || movement.DestinationWarehouseId == w)
            orderby movement.LedgerSequence
            select new
            {
                movement.MovementNumber,
                movement.MovementType,
                movement.WarehouseId,
                movement.DestinationWarehouseId,
                movement.CreatedAtUtc,
                line.Quantity
            }).ToListAsync(cancellationToken);

        var ledger = new List<KardexEntryDto>(rows.Count);
        decimal running = 0;

        foreach (var row in rows)
        {
            decimal change = row.MovementType switch
            {
                MovementType.Entry => row.Quantity,
                MovementType.Exit => -row.Quantity,
                // Transfer: leaving the source is negative, arriving at the destination is positive.
                MovementType.Transfer => row.WarehouseId == w ? -row.Quantity : row.Quantity,
                // Adjustment line carries the new absolute count; the change is the delta.
                MovementType.Adjustment => row.Quantity - running,
                _ => 0
            };

            running += change;

            ledger.Add(new KardexEntryDto(
                row.CreatedAtUtc, row.MovementNumber, row.MovementType.ToString(), w, change, running));
        }

        IEnumerable<KardexEntryDto> result = ledger;
        if (request.FromUtc is { } from)
            result = result.Where(e => e.TimestampUtc >= from);
        if (request.ToUtc is { } to)
            result = result.Where(e => e.TimestampUtc <= to);

        return result.ToList();
    }
}
