using Inventory.Domain.Common;
using Inventory.Domain.Inventory.Events;

namespace Inventory.Domain.Inventory;

/// <summary>
/// Stock of a product in a single warehouse. This is a projection: it is only ever mutated as the
/// result of applying an inventory movement, never edited directly (ADR-001, ADR-002).
/// Negative stock is not allowed.
/// </summary>
public sealed class Stock : AggregateRoot
{
    public Guid ProductId { get; private set; }
    public Guid WarehouseId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal ReservedQuantity { get; private set; }
    public long Version { get; private set; }

    /// <summary>AvailableQuantity = Quantity - ReservedQuantity.</summary>
    public decimal AvailableQuantity => Quantity - ReservedQuantity;

    private Stock() { }

    public Stock(Guid productId, Guid warehouseId)
    {
        ProductId = productId;
        WarehouseId = warehouseId;
        Quantity = 0m;
        ReservedQuantity = 0m;
    }

    public void Increase(decimal amount)
    {
        if (amount <= 0)
            throw new DomainException("Increase amount must be positive.");

        if (amount > InventoryNumericLimits.MaxStockQuantity - Quantity)
            throw new DomainException("Stock quantity exceeds the supported maximum.");

        Quantity += amount;
        Version++;
    }

    public void Decrease(decimal amount)
    {
        if (amount <= 0)
            throw new DomainException("Decrease amount must be positive.");
        if (Quantity - amount < 0)
            throw new DomainException("Negative stock is not allowed.");
        if (Quantity - amount < ReservedQuantity)
            throw new DomainException("Cannot reduce stock below the reserved quantity.");

        Quantity -= amount;
        Version++;
    }

    /// <summary>Sets the physical quantity to an absolute value (used by adjustments).</summary>
    public void AdjustTo(decimal newQuantity)
    {
        if (newQuantity < 0)
            throw new DomainException("Negative stock is not allowed.");
        if (newQuantity > InventoryNumericLimits.MaxStockQuantity)
            throw new DomainException("Stock quantity exceeds the supported maximum.");
        if (newQuantity < ReservedQuantity)
            throw new DomainException("Cannot adjust stock below the reserved quantity.");

        Quantity = newQuantity;
        // Even a count equal to the current quantity must conflict with concurrent writes.
        Version++;
    }

    public void Reserve(decimal amount)
    {
        if (amount <= 0)
            throw new DomainException("Reserve amount must be positive.");
        if (amount > Quantity - ReservedQuantity)
            throw new DomainException("Cannot reserve more than the available quantity.");

        ReservedQuantity += amount;
        Version++;
    }

    public void Release(decimal amount)
    {
        if (amount <= 0)
            throw new DomainException("Release amount must be positive.");
        if (ReservedQuantity - amount < 0)
            throw new DomainException("Cannot release more than the reserved quantity.");

        ReservedQuantity -= amount;
        Version++;
    }

    /// <summary>Raises <see cref="StockBelowMinimumDomainEvent"/> when the quantity drops below the product minimum.</summary>
    public void CheckMinimum(decimal minimumStock)
    {
        if (minimumStock > 0 && Quantity < minimumStock)
            Raise(new StockBelowMinimumDomainEvent(ProductId, WarehouseId, Quantity, minimumStock));
    }
}
