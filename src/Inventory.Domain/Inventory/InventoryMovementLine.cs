using Inventory.Domain.Common;

namespace Inventory.Domain.Inventory;

/// <summary>A single product line within an inventory movement.</summary>
public sealed class InventoryMovementLine : Entity
{
    public Guid MovementId { get; private set; }
    public Guid ProductId { get; private set; }
    public decimal Quantity { get; private set; }
    public decimal UnitCost { get; private set; }

    private InventoryMovementLine() { }

    public InventoryMovementLine(Guid productId, decimal quantity, decimal unitCost)
    {
        if (quantity <= 0)
            throw new DomainException("Movement line quantity must be positive.");
        if (unitCost < 0)
            throw new DomainException("Unit cost cannot be negative.");

        ProductId = productId;
        Quantity = quantity;
        UnitCost = unitCost;
    }
}
