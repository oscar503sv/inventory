using Inventory.Application.Common.Abstractions;
using Inventory.Domain.Common;
using Microsoft.Extensions.Logging;

namespace Inventory.Infrastructure.Events;

/// <summary>
/// Phase 1 in-process dispatcher: logs each domain event. This is the seam where the RabbitMQ
/// publisher is plugged in during Phase 3 (ADR-004) without touching the domain or application.
/// </summary>
public sealed class LoggingDomainEventDispatcher : IDomainEventDispatcher
{
    private readonly ILogger<LoggingDomainEventDispatcher> _logger;

    public LoggingDomainEventDispatcher(ILogger<LoggingDomainEventDispatcher> logger) => _logger = logger;

    public Task DispatchAsync(
        IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
    {
        foreach (var domainEvent in domainEvents)
        {
            _logger.LogInformation(
                "Domain event {EventType} occurred at {OccurredOnUtc:o}: {@Event}",
                domainEvent.GetType().Name, domainEvent.OccurredOnUtc, domainEvent);
        }

        return Task.CompletedTask;
    }
}
