namespace Inventory.Domain.Common;

/// <summary>
/// Marker for an in-process domain event. Raised by aggregate roots and dispatched by the
/// persistence layer after the transaction commits (ADR-004).
/// </summary>
public interface IDomainEvent
{
    DateTime OccurredOnUtc { get; }
}
