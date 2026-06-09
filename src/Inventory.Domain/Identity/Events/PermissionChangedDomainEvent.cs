using Inventory.Domain.Common;

namespace Inventory.Domain.Identity.Events;

public sealed record PermissionChangedDomainEvent(Guid UserId) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
