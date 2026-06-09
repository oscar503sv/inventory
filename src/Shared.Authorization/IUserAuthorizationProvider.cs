namespace Shared.Authorization;

/// <summary>
/// Loads a user's authorization inputs (typically from PostgreSQL, cached in Redis in later phases).
/// </summary>
public interface IUserAuthorizationProvider
{
    Task<UserAuthorizationData> GetAuthorizationDataAsync(Guid userId, CancellationToken cancellationToken = default);
}
