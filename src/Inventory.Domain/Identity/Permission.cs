using Inventory.Domain.Common;

namespace Inventory.Domain.Identity;

/// <summary>
/// A business-oriented permission, identified by its <see cref="Code"/> (e.g. "inventory.entry.create").
/// </summary>
public sealed class Permission : Entity
{
    public string Code { get; private set; } = null!;
    public string Description { get; private set; } = null!;

    private Permission() { }

    public Permission(string code, string description)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Permission code is required.");

        Code = code.Trim().ToLowerInvariant();
        Description = description?.Trim() ?? string.Empty;
    }
}
