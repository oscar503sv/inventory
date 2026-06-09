using Inventory.Domain.Common;

namespace Inventory.Application.Common.Abstractions;

/// <summary>
/// Dispatches domain events collected from aggregate roots after the unit of work commits.
/// In Phase 1 this runs in-process; in Phase 3 it becomes the seam to the RabbitMQ publisher (ADR-004).
/// </summary>
public interface IDomainEventDispatcher
{
    Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}
