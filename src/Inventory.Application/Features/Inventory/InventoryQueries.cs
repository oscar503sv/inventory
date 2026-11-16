using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Queries;
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
        return await query.Take(QueryPaging.MaxPageSize).ToListAsync(ct);
    }
}

public sealed record GetKardexQuery(Guid ProductId, Guid WarehouseId,
    DateTime? FromUtc = null, DateTime? ToUtc = null) : IRequest<IReadOnlyList<KardexEntryDto>>, IDateRangeQuery;

public sealed class GetKardexQueryHandler(IKardexReader reader, IPermissionChecker permissions, IDateTimeProvider clock)
    : IRequestHandler<GetKardexQuery, IReadOnlyList<KardexEntryDto>>
{
    public async Task<IReadOnlyList<KardexEntryDto>> Handle(GetKardexQuery request, CancellationToken ct)
    {
        await permissions.EnsureWarehouseAccessAsync(request.WarehouseId, ct);
        var range = QueryDateRange.Resolve(request, clock.UtcNow);
        return (await reader.ReadAsync(request.ProductId, request.WarehouseId, range.FromUtc, range.ToUtc,
            1, QueryPaging.MaxPageSize, ct)).Items;
    }
}

public sealed class GetStockQueryValidator : AbstractValidator<GetStockQuery>
{
    public GetStockQueryValidator()
    {
        RuleFor(query => query.ProductId).NotEqual(Guid.Empty);
        RuleFor(query => query.WarehouseId).NotEqual(Guid.Empty);
    }
}

public sealed class GetKardexQueryValidator : AbstractValidator<GetKardexQuery>
{
    public GetKardexQueryValidator()
    {
        this.AddDateRangeRules();
        RuleFor(query => query.ProductId).NotEmpty();
        RuleFor(query => query.WarehouseId).NotEmpty();
    }
}
