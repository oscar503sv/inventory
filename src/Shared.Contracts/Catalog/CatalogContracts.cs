namespace Shared.Contracts.Catalog;

public sealed record ProductDto(
    Guid Id,
    string Sku,
    string Code,
    string Name,
    string Description,
    Guid CategoryId,
    Guid BrandId,
    Guid UnitOfMeasureId,
    decimal MinimumStock,
    bool IsActive);

public sealed record CategoryDto(Guid Id, string Name, string Description);

public sealed record BrandDto(Guid Id, string Name, string Description);

public sealed record UnitOfMeasureDto(Guid Id, string Code, string Name);
