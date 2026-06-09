using Inventory.Domain.Common;

namespace Inventory.Domain.Catalog.Events;

public sealed record ProductUpdatedDomainEvent(Guid ProductId) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
