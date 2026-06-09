using Inventory.Domain.Common;

namespace Inventory.Domain.Identity;

/// <summary>A role that groups a set of default permissions (ADR-003).</summary>
public sealed class Role : Entity
{
    private readonly List<RolePermission> _permissions = new();

    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public IReadOnlyCollection<RolePermission> Permissions => _permissions.AsReadOnly();

    private Role() { }

    public Role(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Role name is required.");

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
    }

    public void GrantPermission(Guid permissionId)
    {
        if (_permissions.Any(p => p.PermissionId == permissionId))
            return;

        _permissions.Add(new RolePermission(Id, permissionId));
    }
}
