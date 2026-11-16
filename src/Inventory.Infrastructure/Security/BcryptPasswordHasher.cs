using Inventory.Application.Common.Abstractions;

namespace Inventory.Infrastructure.Security;

/// <summary>BCrypt-based password hashing (PRD: Security — secure password hashing).</summary>
public sealed class BcryptPasswordHasher : IPasswordHasher
{
    private const int WorkFactor = 12;
    // Non-secret placeholder, precomputed so missing users still pay one verification at the real cost.
    public string DummyHash => "$2a$12$TTO8a5Rw3oa3aNGFonqeZuB3PoO0vpboy7dDvzsUxcsdMHGobBt1G";

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, workFactor: WorkFactor);

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
