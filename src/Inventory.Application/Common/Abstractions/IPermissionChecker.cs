using Shared.Authorization;

namespace Inventory.Application.Common.Abstractions;

/// <summary>
/// Contextual authorization for the current request — used by handlers to enforce warehouse scope
/// (ADR-003), which cannot be checked by a static permission policy alone.
/// </summary>
public interface IPermissionChecker
{
    Task<UserAuthorizationData> GetCurrentUserAuthorizationAsync(CancellationToken cancellationToken = default);

    Task<bool> CanAccessWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken = default);

    /// <summary>Throws <see cref="Exceptions.ForbiddenAccessException"/> when the warehouse is out of scope.</summary>
    Task EnsureWarehouseAccessAsync(Guid warehouseId, CancellationToken cancellationToken = default);
}
