using Inventory.Application.Common.Abstractions;

namespace Inventory.Infrastructure.Security;

/// <summary>BCrypt-based password hashing (PRD: Security — secure password hashing).</summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);

    public bool Verify(string password, string passwordHash)
    {
        try
        {
            return BCrypt.Net.BCrypt.Verify(password, passwordHash);
        }
        catch
        {
            return false;
        }
    }
}
