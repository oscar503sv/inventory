namespace Inventory.Application.Common.Abstractions;

/// <summary>Secure password hashing (PRD: Security — secure password hashing).</summary>
public interface IPasswordHasher
{
    /// <summary>A precomputed hash with the same cost as real passwords, used for missing users.</summary>
    string DummyHash { get; }
    string Hash(string password);
    bool Verify(string password, string passwordHash);
}
