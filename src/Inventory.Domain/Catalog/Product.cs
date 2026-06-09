using Inventory.Domain.Catalog.Events;
using Inventory.Domain.Common;

namespace Inventory.Domain.Catalog;

/// <summary>
/// A catalog product. Physical quantities are never stored here — stock lives per warehouse in the
/// Inventory context and is only ever changed through movements (ADR-001).
/// </summary>
public sealed class Product : AggregateRoot
{
    public string Sku { get; private set; } = null!;
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public Guid CategoryId { get; private set; }
    public Guid BrandId { get; private set; }
    public Guid UnitOfMeasureId { get; private set; }
    public decimal MinimumStock { get; private set; }
    public bool IsActive { get; private set; }

    private Product() { }

    public static Product Create(
        string sku,
        string code,
        string name,
        string description,
        Guid categoryId,
        Guid brandId,
        Guid unitOfMeasureId,
        decimal minimumStock)
    {
        if (string.IsNullOrWhiteSpace(sku))
            throw new DomainException("SKU is required.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Product name is required.");
        if (minimumStock < 0)
            throw new DomainException("Minimum stock cannot be negative.");

        var product = new Product
        {
            Sku = sku.Trim(),
            Code = string.IsNullOrWhiteSpace(code) ? sku.Trim() : code.Trim(),
            Name = name.Trim(),
            Description = description?.Trim() ?? string.Empty,
            CategoryId = categoryId,
            BrandId = brandId,
            UnitOfMeasureId = unitOfMeasureId,
            MinimumStock = minimumStock,
            IsActive = true
        };

        product.Raise(new ProductCreatedDomainEvent(product.Id, product.Sku));
        return product;
    }

    public void Update(
        string name,
        string description,
        Guid categoryId,
        Guid brandId,
        Guid unitOfMeasureId,
        decimal minimumStock)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Product name is required.");
        if (minimumStock < 0)
            throw new DomainException("Minimum stock cannot be negative.");

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
        CategoryId = categoryId;
        BrandId = brandId;
        UnitOfMeasureId = unitOfMeasureId;
        MinimumStock = minimumStock;

        Raise(new ProductUpdatedDomainEvent(Id));
    }

    public void Activate() => IsActive = true;
    public void Deactivate() => IsActive = false;
}
