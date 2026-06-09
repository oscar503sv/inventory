using Inventory.Domain.Common;

namespace Inventory.Domain.Inventory.Events;

public sealed record InventoryEntryCreatedDomainEvent(Guid MovementId, Guid WarehouseId) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

public sealed record InventoryExitCreatedDomainEvent(Guid MovementId, Guid WarehouseId) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

public sealed record InventoryAdjustedDomainEvent(Guid MovementId, Guid WarehouseId) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

public sealed record InventoryTransferCreatedDomainEvent(
    Guid MovementId,
    Guid SourceWarehouseId,
    Guid DestinationWarehouseId) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}

public sealed record StockBelowMinimumDomainEvent(
    Guid ProductId,
    Guid WarehouseId,
    decimal Quantity,
    decimal MinimumStock) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
