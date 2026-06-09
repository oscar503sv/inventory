using Inventory.Application.Common.Abstractions;
using Inventory.Domain.Inventory;

namespace Inventory.Infrastructure.Inventory;

/// <summary>
/// Produces unique, human-readable movement numbers such as <c>ENT-20260608-1A2B3C4D</c>.
/// </summary>
public sealed class MovementNumberGenerator : IMovementNumberGenerator
{
    private readonly IDateTimeProvider _clock;

    public MovementNumberGenerator(IDateTimeProvider clock) => _clock = clock;

    public Task<string> NextAsync(MovementType type, CancellationToken cancellationToken = default)
    {
        var prefix = type switch
        {
            MovementType.Entry => "ENT",
            MovementType.Exit => "EXI",
            MovementType.Transfer => "TRF",
            MovementType.Adjustment => "ADJ",
            _ => "MOV"
        };

        var suffix = Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
        var number = $"{prefix}-{_clock.UtcNow:yyyyMMdd}-{suffix}";
        return Task.FromResult(number);
    }
}
