using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Inventory.Application.Common.Queries;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Catalog;
using Shared.Contracts.Common;

namespace Inventory.Application.Features.Products;

public sealed record GetProductsQuery(
    string? Search = null,
    bool? IsActive = null,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<ProductDto>>;

public sealed class GetProductsQueryHandler : IRequestHandler<GetProductsQuery, PagedResult<ProductDto>>
{
    private readonly IApplicationDbContext _db;

    public GetProductsQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<PagedResult<ProductDto>> Handle(GetProductsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Clamp(request.Page, 1, QueryPaging.MaxPage);
        var pageSize = Math.Clamp(request.PageSize, 1, QueryPaging.MaxPageSize);

        var query = _db.Products.AsNoTracking();

        if (request.IsActive is { } isActive)
            query = query.Where(p => p.IsActive == isActive);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = request.Search.Trim().ToLower();
            query = query.Where(p =>
                p.Sku.ToLower().Contains(term) ||
                p.Name.ToLower().Contains(term) ||
                p.Code.ToLower().Contains(term));
        }

        return await query
            .OrderBy(p => p.Name)
            .ThenBy(p => p.Id)
            .Select(p => new ProductDto(
                p.Id, p.Sku, p.Code, p.Name, p.Description,
                p.CategoryId, p.BrandId, p.UnitOfMeasureId, p.MinimumStock, p.IsActive))
            .ToPageAsync(page, pageSize, cancellationToken);
    }
}

public sealed record GetProductByIdQuery(Guid Id) : IRequest<ProductDto>;

public sealed class GetProductByIdQueryHandler : IRequestHandler<GetProductByIdQuery, ProductDto>
{
    private readonly IApplicationDbContext _db;

    public GetProductByIdQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<ProductDto> Handle(GetProductByIdQuery request, CancellationToken cancellationToken)
    {
        var product = await _db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == request.Id, cancellationToken)
            ?? throw NotFoundException.For("Product", request.Id);

        return product.ToDto();
    }
}
