using System.Security.Cryptography;
using Inventory.Application.Common.Abstractions;
using Microsoft.Extensions.Options;

namespace Inventory.Infrastructure.Security;

/// <summary>
/// Generates cryptographically random opaque refresh tokens and stores only their SHA-256 hash,
/// so a database leak does not expose usable tokens.
/// </summary>
public sealed class RefreshTokenService : IRefreshTokenService
{
    private readonly JwtOptions _options;

    public RefreshTokenService(IOptions<JwtOptions> options) => _options = options.Value;

    public (string Token, string TokenHash) Generate()
    {
        var bytes = RandomNumberGenerator.GetBytes(64);
        var token = Convert.ToBase64String(bytes);
        return (token, Hash(token));
    }

    public string Hash(string token)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(hash);
    }

    public DateTime GetRefreshTokenExpiry(DateTime issuedAtUtc) => issuedAtUtc.AddDays(_options.RefreshTokenDays);
}
