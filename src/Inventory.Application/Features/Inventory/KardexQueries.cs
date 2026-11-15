using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Queries;
using Inventory.Domain.Inventory;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Common;
using Shared.Contracts.Inventory;

namespace Inventory.Application.Features.Inventory;

public sealed record GetKardexPageQuery(Guid ProductId, Guid WarehouseId,
    DateTime? FromUtc = null, DateTime? ToUtc = null, int Page = 1, int PageSize = QueryPaging.DefaultPageSize)
    : IRequest<PagedResult<KardexEntryDto>>, IPagedQuery, IDateRangeQuery;

public sealed class GetKardexPageQueryValidator : PagedQueryValidator<GetKardexPageQuery>
{
    public GetKardexPageQueryValidator()
    {
        this.AddDateRangeRules();
        RuleFor(q => q.ProductId).NotEmpty();
        RuleFor(q => q.WarehouseId).NotEmpty();
    }
}

internal sealed class LedgerRow
{
    public long Sequence { get; init; }
    public string Number { get; init; } = "";
    public MovementType Type { get; init; }
    public DateTime TimestampUtc { get; init; }
    public decimal Quantity { get; init; }
    public decimal Delta { get; init; }
}

internal static class KardexQueries
{
    public static IQueryable<LedgerRow> Rows(IApplicationDbContext db, Guid product, Guid warehouse)
        => from line in db.InventoryMovementLines.AsNoTracking()
           join movement in db.InventoryMovements on line.MovementId equals movement.Id
           where line.ProductId == product && (movement.WarehouseId == warehouse || movement.DestinationWarehouseId == warehouse)
           select new LedgerRow
           {
               Sequence = movement.LedgerSequence,
               Number = movement.MovementNumber,
               Type = movement.MovementType,
               TimestampUtc = movement.CreatedAtUtc,
               Quantity = line.Quantity,
               Delta = movement.MovementType == MovementType.Entry ? line.Quantity
                   : movement.MovementType == MovementType.Exit ? -line.Quantity
                   : movement.MovementType == MovementType.Transfer
                       ? movement.WarehouseId == warehouse ? -line.Quantity : line.Quantity : 0
           };

    public static IQueryable<LedgerRow> Filter(IQueryable<LedgerRow> rows, DateTime? fromUtc, DateTime? toUtc)
    {
        if (fromUtc is { } from) rows = rows.Where(r => r.TimestampUtc >= from);
        if (toUtc is { } to) rows = rows.Where(r => r.TimestampUtc <= to);
        return rows.OrderBy(r => r.Sequence);
    }

    public static IQueryable<KardexEntryDto> WithBalances(
        IQueryable<LedgerRow> history, IQueryable<LedgerRow> page, Guid warehouse)
        => from row in page
           let anchor = history.Where(p => p.Type == MovementType.Adjustment && p.Sequence < row.Sequence)
               .Select(p => (long?)p.Sequence).Max() ?? 0
           let count = history.Where(p => anchor > 0 && p.Sequence == anchor).Select(p => (decimal?)p.Quantity).FirstOrDefault() ?? 0
           // The opening balance comes from the full history, including rows outside the date/page filter.
           let opening = count + (history.Where(p => p.Sequence > anchor && p.Sequence < row.Sequence)
               .Sum(p => (decimal?)p.Delta) ?? 0)
           select new KardexEntryDto(row.TimestampUtc, row.Number, row.Type.ToString(), warehouse,
               row.Type == MovementType.Adjustment ? row.Quantity - opening : row.Delta,
               row.Type == MovementType.Adjustment ? row.Quantity : opening + row.Delta);
}

public sealed class GetKardexPageQueryHandler(IApplicationDbContext db, IPermissionChecker permissions)
    : IRequestHandler<GetKardexPageQuery, PagedResult<KardexEntryDto>>
{
    public async Task<PagedResult<KardexEntryDto>> Handle(GetKardexPageQuery request, CancellationToken ct)
    {
        await permissions.EnsureWarehouseAccessAsync(request.WarehouseId, ct);
        var history = KardexQueries.Rows(db, request.ProductId, request.WarehouseId);
        var filtered = KardexQueries.Filter(history, request.FromUtc, request.ToUtc);
        var total = await filtered.CountAsync(ct);
        var page = filtered.Skip(QueryPaging.Offset(request.Page, request.PageSize)).Take(request.PageSize);
        var items = await KardexQueries.WithBalances(history, page, request.WarehouseId).ToListAsync(ct);
        return new PagedResult<KardexEntryDto>(items, request.Page, request.PageSize, total);
    }
}
