using Inventory.Domain.Common;

namespace Inventory.Domain.Identity;

/// <summary>
/// A hashed refresh token. Supports rotation: when used, it is revoked and linked to its replacement.
/// </summary>
public sealed class RefreshToken : Entity
{
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = null!;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? ReplacedByTokenHash { get; private set; }

    public bool IsActive(DateTime nowUtc) => RevokedAtUtc is null && nowUtc < ExpiresAtUtc;

    private RefreshToken() { }

    public RefreshToken(Guid userId, string tokenHash, DateTime createdAtUtc, DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(tokenHash))
            throw new DomainException("Refresh token hash is required.");
        if (expiresAtUtc <= createdAtUtc)
            throw new DomainException("Refresh token expiry must be after its creation.");

        UserId = userId;
        TokenHash = tokenHash;
        CreatedAtUtc = createdAtUtc;
        ExpiresAtUtc = expiresAtUtc;
    }

    public void Revoke(DateTime nowUtc, string? replacedByTokenHash = null)
    {
        if (RevokedAtUtc is not null)
            return;

        RevokedAtUtc = nowUtc;
        ReplacedByTokenHash = replacedByTokenHash;
    }
}
