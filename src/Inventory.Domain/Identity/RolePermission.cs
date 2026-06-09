using Inventory.Domain.Common;

namespace Inventory.Domain.Identity;

/// <summary>Join entity assigning a <see cref="Permission"/> to a <see cref="Role"/>.</summary>
public sealed class RolePermission : Entity
{
    public Guid RoleId { get; private set; }
    public Guid PermissionId { get; private set; }

    private RolePermission() { }

    public RolePermission(Guid roleId, Guid permissionId)
    {
        RoleId = roleId;
        PermissionId = permissionId;
    }
}
