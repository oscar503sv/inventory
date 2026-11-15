using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Queries;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Common;
using Shared.Contracts.Inventory;

namespace Inventory.Application.Features.Inventory;

public sealed record GetStockPageQuery(Guid? ProductId = null, Guid? WarehouseId = null,
    int Page = 1, int PageSize = QueryPaging.DefaultPageSize) : IRequest<PagedResult<StockDto>>, IPagedQuery;

public sealed class GetStockPageQueryValidator : PagedQueryValidator<GetStockPageQuery>
{
    public GetStockPageQueryValidator()
    {
        RuleFor(q => q.ProductId).NotEqual(Guid.Empty);
        RuleFor(q => q.WarehouseId).NotEqual(Guid.Empty);
    }
}

internal static class StockQueries
{
    public static async Task<IQueryable<StockDto>> BuildAsync(IApplicationDbContext db,
        IPermissionChecker permissions, Guid? productId, Guid? warehouseId, CancellationToken ct)
    {
        if (warehouseId is { } requested) await permissions.EnsureWarehouseAccessAsync(requested, ct);
        var access = await permissions.GetCurrentUserAuthorizationAsync(ct);
        var query = db.Stocks.AsNoTracking();
        if (!access.HasAllWarehousesAccess) query = query.Where(s => access.WarehouseScopes.Contains(s.WarehouseId));
        if (productId is { } product) query = query.Where(s => s.ProductId == product);
        if (warehouseId is { } warehouse) query = query.Where(s => s.WarehouseId == warehouse);
        return query.OrderBy(s => s.WarehouseId).ThenBy(s => s.ProductId)
            .Select(s => new StockDto(s.ProductId, s.WarehouseId, s.Quantity, s.ReservedQuantity,
                s.Quantity - s.ReservedQuantity));
    }
}

public sealed class GetStockPageQueryHandler(IApplicationDbContext db, IPermissionChecker permissions)
    : IRequestHandler<GetStockPageQuery, PagedResult<StockDto>>
{
    public async Task<PagedResult<StockDto>> Handle(GetStockPageQuery request, CancellationToken ct)
    {
        var query = await StockQueries.BuildAsync(db, permissions, request.ProductId, request.WarehouseId, ct);
        return await query.ToPageAsync(request.Page, request.PageSize, ct);
    }
}
