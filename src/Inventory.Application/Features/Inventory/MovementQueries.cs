using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Inventory.Application.Common.Queries;
using Inventory.Domain.Inventory;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Common;
using Shared.Contracts.Inventory;

namespace Inventory.Application.Features.Inventory;

public sealed record GetMovementsQuery(Guid? WarehouseId = null, Guid? ProductId = null,
    MovementType? Type = null, DateTime? FromUtc = null, DateTime? ToUtc = null,
    int Page = 1, int PageSize = QueryPaging.DefaultPageSize)
    : IRequest<PagedResult<MovementSummaryDto>>, IPagedQuery, IDateRangeQuery;

public sealed class GetMovementsQueryValidator : PagedQueryValidator<GetMovementsQuery>
{
    public GetMovementsQueryValidator()
    {
        this.AddDateRangeRules();
        RuleFor(q => q.WarehouseId).NotEqual(Guid.Empty);
        RuleFor(q => q.ProductId).NotEqual(Guid.Empty);
        RuleFor(q => q.Type).IsInEnum();
    }
}

internal static class MovementQueries
{
    public static async Task<IQueryable<InventoryMovement>> VisibleAsync(IApplicationDbContext db,
        IPermissionChecker permissions, Guid? warehouse, CancellationToken ct)
    {
        if (warehouse is { } requested) await permissions.EnsureWarehouseAccessAsync(requested, ct);
        var access = await permissions.GetCurrentUserAuthorizationAsync(ct);
        var query = db.InventoryMovements.AsNoTracking();
        // A full movement reveals both warehouses, so a transfer requires access to both ends.
        if (!access.HasAllWarehousesAccess)
            query = query.Where(m => access.WarehouseScopes.Contains(m.WarehouseId)
                && (m.DestinationWarehouseId == null || access.WarehouseScopes.Contains(m.DestinationWarehouseId.Value)));
        if (warehouse is { } id) query = query.Where(m => m.WarehouseId == id || m.DestinationWarehouseId == id);
        return query;
    }

    public static IQueryable<InventoryMovement> Filter(IQueryable<InventoryMovement> query, GetMovementsQuery request)
    {
        if (request.ProductId is { } product) query = query.Where(m => m.Lines.Any(l => l.ProductId == product));
        if (request.Type is { } type) query = query.Where(m => m.MovementType == type);
        if (request.FromUtc is { } from) query = query.Where(m => m.CreatedAtUtc >= from);
        if (request.ToUtc is { } to) query = query.Where(m => m.CreatedAtUtc <= to);
        return query;
    }
}

public sealed class GetMovementsQueryHandler(IApplicationDbContext db, IPermissionChecker permissions)
    : IRequestHandler<GetMovementsQuery, PagedResult<MovementSummaryDto>>
{
    public async Task<PagedResult<MovementSummaryDto>> Handle(GetMovementsQuery request, CancellationToken ct)
    {
        var visible = await MovementQueries.VisibleAsync(db, permissions, request.WarehouseId, ct);
        return await MovementQueries.Filter(visible, request).OrderByDescending(m => m.LedgerSequence)
            .Select(m => new MovementSummaryDto(m.Id, m.MovementNumber, m.MovementType.ToString(), m.WarehouseId,
                m.DestinationWarehouseId, m.Status.ToString(), m.CreatedBy, m.CreatedAtUtc, m.Notes, m.Lines.Count))
            .ToPageAsync(request.Page, request.PageSize, ct);
    }
}

public sealed record GetMovementByIdQuery(Guid Id) : IRequest<MovementDto>;

public sealed class GetMovementByIdQueryValidator : AbstractValidator<GetMovementByIdQuery>
{
    public GetMovementByIdQueryValidator() => RuleFor(q => q.Id).NotEmpty();
}

public sealed class GetMovementByIdQueryHandler(IApplicationDbContext db, IPermissionChecker permissions)
    : IRequestHandler<GetMovementByIdQuery, MovementDto>
{
    public async Task<MovementDto> Handle(GetMovementByIdQuery request, CancellationToken ct)
    {
        var movement = await db.InventoryMovements.AsNoTracking().Include(m => m.Lines)
            .SingleOrDefaultAsync(m => m.Id == request.Id, ct) ?? throw NotFoundException.For("Movement", request.Id);
        await permissions.EnsureWarehouseAccessAsync(movement.WarehouseId, ct);
        if (movement.DestinationWarehouseId is { } destination)
            await permissions.EnsureWarehouseAccessAsync(destination, ct);
        return movement.ToDto();
    }
}
