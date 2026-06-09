namespace Inventory.Domain.Inventory;

/// <summary>The kind of inventory movement (ADR-001). Every stock change is one of these.</summary>
public enum MovementType
{
    Entry = 1,
    Exit = 2,
    Transfer = 3,
    Adjustment = 4
}
