namespace Inventory.Application.Common.Abstractions;

/// <summary>Issues signed JWT access tokens (PRD: JWT Access Tokens).</summary>
public interface IJwtTokenGenerator
{
    string GenerateAccessToken(Guid userId, string email, IEnumerable<string> roles);
    DateTime GetAccessTokenExpiry(DateTime issuedAtUtc);
}

/// <summary>Generates opaque refresh tokens and their stored hashes (PRD: refresh token rotation).</summary>
public interface IRefreshTokenService
{
    /// <summary>Returns a new opaque token (sent to the client) and its hash (stored in the database).</summary>
    (string Token, string TokenHash) Generate();
    string Hash(string token);
    DateTime GetRefreshTokenExpiry(DateTime issuedAtUtc);
}
