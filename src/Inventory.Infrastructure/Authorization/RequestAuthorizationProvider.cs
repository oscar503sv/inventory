using Shared.Authorization;

namespace Inventory.Infrastructure.Authorization;

/// <summary>Shares successful authorization loads across every consumer in one DI request scope.</summary>
public sealed class RequestAuthorizationProvider(UserAuthorizationProvider inner) : IUserAuthorizationProvider, IDisposable
{
    private readonly Dictionary<Guid, Task<UserAuthorizationData>> _loads = [];
    private readonly SemaphoreSlim _databaseGate = new(1, 1);

    public Task<UserAuthorizationData> GetAuthorizationDataAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Task<UserAuthorizationData> load;
        lock (_loads)
        {
            if (!_loads.TryGetValue(userId, out load!) || load.IsFaulted || load.IsCanceled)
            {
                load = LoadAsync(userId, cancellationToken);
                _loads[userId] = load;
            }
        }
        return load.WaitAsync(cancellationToken);
    }

    public void Invalidate(Guid userId)
    {
        lock (_loads) _loads.Remove(userId);
    }

    private async Task<UserAuthorizationData> LoadAsync(Guid userId, CancellationToken ct)
    {
        // A scoped DbContext cannot execute concurrent queries, even for different users.
        await _databaseGate.WaitAsync(ct);
        try { return await inner.GetAuthorizationDataAsync(userId, ct); }
        finally { _databaseGate.Release(); }
    }

    public void Dispose() => _databaseGate.Dispose();
}
