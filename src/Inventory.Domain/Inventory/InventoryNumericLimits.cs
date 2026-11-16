namespace Inventory.Domain.Inventory;

public static class InventoryNumericLimits
{
    public const int Precision = 18;
    public const int Scale = 4;
    public const decimal MaxStockQuantity = 99999999999999.9999m;
    public const int MaxNotesLength = 1024;
}
