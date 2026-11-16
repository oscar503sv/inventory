using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Shared.Authorization;

namespace Inventory.Infrastructure.Authorization;

/// <summary>
/// Per-request contextual authorization. Caches the current user's authorization data for the
/// lifetime of the request (scoped) to avoid repeated database round-trips.
/// </summary>
public sealed class PermissionChecker : IPermissionChecker
{
    private readonly ICurrentUser _currentUser;
    private readonly IUserAuthorizationProvider _provider;

    public PermissionChecker(ICurrentUser currentUser, IUserAuthorizationProvider provider)
    {
        _currentUser = currentUser;
        _provider = provider;
    }

    public async Task<UserAuthorizationData> GetCurrentUserAuthorizationAsync(CancellationToken cancellationToken = default)
    {
        if (_currentUser.UserId is not { } userId)
            throw new ForbiddenAccessException("No authenticated user.");

        return await _provider.GetAuthorizationDataAsync(userId, cancellationToken);
    }

    public async Task<bool> CanAccessWarehouseAsync(Guid warehouseId, CancellationToken cancellationToken = default)
    {
        var data = await GetCurrentUserAuthorizationAsync(cancellationToken);
        return PermissionEngine.CanAccessWarehouse(data, warehouseId);
    }

    public async Task EnsureWarehouseAccessAsync(Guid warehouseId, CancellationToken cancellationToken = default)
    {
        if (!await CanAccessWarehouseAsync(warehouseId, cancellationToken))
            throw new ForbiddenAccessException($"You are not authorized for warehouse '{warehouseId}'.");
    }
}
