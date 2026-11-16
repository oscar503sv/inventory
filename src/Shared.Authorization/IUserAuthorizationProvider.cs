namespace Shared.Authorization;

/// <summary>
/// Loads a user's authorization inputs (typically from PostgreSQL, cached in Redis in later phases).
/// </summary>
public interface IUserAuthorizationProvider
{
    /// <summary>Discard a snapshot before rechecking access inside a protected write transaction.</summary>
    void Invalidate(Guid userId) { }

    Task<UserAuthorizationData> GetAuthorizationDataAsync(Guid userId, CancellationToken cancellationToken = default);
}
