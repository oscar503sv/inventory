using Inventory.Domain.Common;

namespace Inventory.Domain.Identity;

/// <summary>
/// Restricts a user's permissions to a specific warehouse. A user with no scopes and
/// <see cref="User.HasAllWarehousesAccess"/> = true may act on every warehouse (ADR-003).
/// </summary>
public sealed class WarehouseScope : Entity
{
    public Guid UserId { get; private set; }
    public Guid WarehouseId { get; private set; }

    private WarehouseScope() { }

    public WarehouseScope(Guid userId, Guid warehouseId)
    {
        UserId = userId;
        WarehouseId = warehouseId;
    }
}
