namespace Inventory.Application.Common.Abstractions;

/// <summary>Secure password hashing (PRD: Security — secure password hashing).</summary>
public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}
