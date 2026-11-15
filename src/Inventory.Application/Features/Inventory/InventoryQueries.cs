using Inventory.Application.Common.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Inventory;

namespace Inventory.Application.Features.Inventory;

public sealed record GetStockQuery(Guid? ProductId = null, Guid? WarehouseId = null) : IRequest<IReadOnlyList<StockDto>>;

public sealed class GetStockQueryHandler(IApplicationDbContext db, IPermissionChecker permissions)
    : IRequestHandler<GetStockQuery, IReadOnlyList<StockDto>>
{
    public async Task<IReadOnlyList<StockDto>> Handle(GetStockQuery request, CancellationToken ct)
    {
        var query = await StockQueries.BuildAsync(db, permissions, request.ProductId, request.WarehouseId, ct);
        return await query.ToListAsync(ct);
    }
}

public sealed record GetKardexQuery(Guid ProductId, Guid WarehouseId,
    DateTime? FromUtc = null, DateTime? ToUtc = null) : IRequest<IReadOnlyList<KardexEntryDto>>;

public sealed class GetKardexQueryHandler(IApplicationDbContext db, IPermissionChecker permissions)
    : IRequestHandler<GetKardexQuery, IReadOnlyList<KardexEntryDto>>
{
    public async Task<IReadOnlyList<KardexEntryDto>> Handle(GetKardexQuery request, CancellationToken ct)
    {
        await permissions.EnsureWarehouseAccessAsync(request.WarehouseId, ct);
        var history = KardexQueries.Rows(db, request.ProductId, request.WarehouseId);
        var filtered = KardexQueries.Filter(history, request.FromUtc, request.ToUtc);
        return await KardexQueries.WithBalances(history, filtered, request.WarehouseId).ToListAsync(ct);
    }
}
