using Inventory.Domain.Common;

namespace Inventory.Domain.Catalog.Events;

public sealed record ProductCreatedDomainEvent(Guid ProductId, string Sku) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
