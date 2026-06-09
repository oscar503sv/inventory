using Inventory.Domain.Common;
using Inventory.Domain.Inventory.Events;

namespace Inventory.Domain.Inventory;

/// <summary>
/// An inventory movement — the only way stock ever changes (ADR-001). Movements are immutable once
/// created: there are no mutators for lines and the persistence layer never deletes them.
/// Every movement requires a user and a timestamp.
/// </summary>
public sealed class InventoryMovement : AggregateRoot
{
    private readonly List<InventoryMovementLine> _lines = new();

    public string MovementNumber { get; private set; } = null!;
    public MovementType MovementType { get; private set; }
    public Guid WarehouseId { get; private set; }

    /// <summary>Destination warehouse — only set for <see cref="MovementType.Transfer"/>.</summary>
    public Guid? DestinationWarehouseId { get; private set; }

    public MovementStatus Status { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public string Notes { get; private set; } = string.Empty;

    public IReadOnlyCollection<InventoryMovementLine> Lines => _lines.AsReadOnly();

    private InventoryMovement() { }

    private InventoryMovement(
        string movementNumber,
        MovementType type,
        Guid warehouseId,
        Guid createdBy,
        DateTime createdAtUtc,
        IEnumerable<InventoryMovementLine> lines,
        string? notes,
        Guid? destinationWarehouseId = null)
    {
        if (string.IsNullOrWhiteSpace(movementNumber))
            throw new DomainException("Movement number is required.");
        if (warehouseId == Guid.Empty)
            throw new DomainException("Warehouse is required.");
        if (createdBy == Guid.Empty)
            throw new DomainException("Every movement requires a user.");
        if (createdAtUtc == default)
            throw new DomainException("Every movement requires a timestamp.");

        _lines.AddRange(lines);
        if (_lines.Count == 0)
            throw new DomainException("A movement must contain at least one line.");

        MovementNumber = movementNumber.Trim();
        MovementType = type;
        WarehouseId = warehouseId;
        DestinationWarehouseId = destinationWarehouseId;
        CreatedBy = createdBy;
        CreatedAtUtc = createdAtUtc;
        Notes = notes?.Trim() ?? string.Empty;
        Status = MovementStatus.Confirmed;
    }

    public static InventoryMovement CreateEntry(
        string movementNumber, Guid warehouseId, Guid createdBy, DateTime createdAtUtc,
        IEnumerable<InventoryMovementLine> lines, string? notes = null)
    {
        var movement = new InventoryMovement(
            movementNumber, MovementType.Entry, warehouseId, createdBy, createdAtUtc, lines, notes);
        movement.Raise(new InventoryEntryCreatedDomainEvent(movement.Id, warehouseId));
        return movement;
    }

    public static InventoryMovement CreateExit(
        string movementNumber, Guid warehouseId, Guid createdBy, DateTime createdAtUtc,
        IEnumerable<InventoryMovementLine> lines, string? notes = null)
    {
        var movement = new InventoryMovement(
            movementNumber, MovementType.Exit, warehouseId, createdBy, createdAtUtc, lines, notes);
        movement.Raise(new InventoryExitCreatedDomainEvent(movement.Id, warehouseId));
        return movement;
    }

    public static InventoryMovement CreateAdjustment(
        string movementNumber, Guid warehouseId, Guid createdBy, DateTime createdAtUtc,
        IEnumerable<InventoryMovementLine> lines, string justification)
    {
        if (string.IsNullOrWhiteSpace(justification))
            throw new DomainException("Adjustments require a justification.");

        var movement = new InventoryMovement(
            movementNumber, MovementType.Adjustment, warehouseId, createdBy, createdAtUtc, lines, justification);
        movement.Raise(new InventoryAdjustedDomainEvent(movement.Id, warehouseId));
        return movement;
    }

    public static InventoryMovement CreateTransfer(
        string movementNumber, Guid sourceWarehouseId, Guid destinationWarehouseId,
        Guid createdBy, DateTime createdAtUtc, IEnumerable<InventoryMovementLine> lines, string? notes = null)
    {
        if (destinationWarehouseId == Guid.Empty)
            throw new DomainException("Transfer destination warehouse is required.");
        if (sourceWarehouseId == destinationWarehouseId)
            throw new DomainException("Transfer source and destination warehouses must differ.");

        var movement = new InventoryMovement(
            movementNumber, MovementType.Transfer, sourceWarehouseId, createdBy, createdAtUtc, lines, notes,
            destinationWarehouseId);
        movement.Raise(new InventoryTransferCreatedDomainEvent(
            movement.Id, sourceWarehouseId, destinationWarehouseId));
        return movement;
    }
}
