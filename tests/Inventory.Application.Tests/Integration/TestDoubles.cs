using Inventory.Application.Common.Abstractions;
using Inventory.Domain.Common;
using Inventory.Domain.Inventory;
using Shared.Authorization;

namespace Inventory.Application.Tests.Integration;

internal sealed class TestClock : IDateTimeProvider
{
    private DateTime _now = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    public DateTime UtcNow => _now = _now.AddSeconds(1); // strictly increasing for deterministic ordering
}

internal sealed class TestCurrentUser : ICurrentUser
{
    public Guid? UserId { get; } = Guid.NewGuid();
    public string? Email => "tester@inventory.local";
    public string? IpAddress => "127.0.0.1";
    public string? CorrelationId => "test-correlation";
    public bool IsAuthenticated => true;
}

internal sealed class SequentialMovementNumberGenerator : IMovementNumberGenerator
{
    private int _counter;
    public Task<string> NextAsync(MovementType type, CancellationToken cancellationToken = default)
        => Task.FromResult($"{type}-{++_counter:D4}");
}

/// <summary>Allows every warehouse — exercises the handler path without authorization wiring.</summary>
internal sealed class AllowAllPermissionChecker : IPermissionChecker
{
    public Task<UserAuthorizationData> GetCurrentUserAuthorizationAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new UserAuthorizationData(
            new HashSet<string>(), new HashSet<string>(), new HashSet<string>(), true, new HashSet<Guid>()));

    public Task<bool> CanAccessWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task EnsureWarehouseAccessAsync(Guid warehouseId, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}

internal sealed class NoOpDomainEventDispatcher : IDomainEventDispatcher
{
    public Task DispatchAsync(IReadOnlyCollection<IDomainEvent> domainEvents, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
