using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Queries;
using MediatR;
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

public sealed class GetKardexPageQueryHandler(IKardexReader reader, IPermissionChecker permissions)
    : IRequestHandler<GetKardexPageQuery, PagedResult<KardexEntryDto>>
{
    public async Task<PagedResult<KardexEntryDto>> Handle(GetKardexPageQuery request, CancellationToken ct)
    {
        await permissions.EnsureWarehouseAccessAsync(request.WarehouseId, ct);
        return await reader.ReadAsync(request.ProductId, request.WarehouseId, request.FromUtc, request.ToUtc,
            request.Page, request.PageSize, ct);
    }
}
