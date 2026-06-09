using Inventory.Domain.Common;

namespace Inventory.Domain.Identity.Events;

public sealed record UserCreatedDomainEvent(Guid UserId, string Email) : IDomainEvent
{
    public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
}
