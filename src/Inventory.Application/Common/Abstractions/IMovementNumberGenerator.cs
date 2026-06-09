using Inventory.Domain.Inventory;

namespace Inventory.Application.Common.Abstractions;

/// <summary>Produces unique, human-readable movement numbers (e.g. ENT-20260608-0001).</summary>
public interface IMovementNumberGenerator
{
    Task<string> NextAsync(MovementType type, CancellationToken cancellationToken = default);
}
