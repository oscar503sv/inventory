using Inventory.Domain.Catalog;
using Shared.Contracts.Catalog;

namespace Inventory.Application.Features.Products;

internal static class ProductMapping
{
    public static ProductDto ToDto(this Product p) => new(
        p.Id, p.Sku, p.Code, p.Name, p.Description,
        p.CategoryId, p.BrandId, p.UnitOfMeasureId, p.MinimumStock, p.IsActive);
}
