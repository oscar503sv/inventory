using Inventory.Domain.Common;

namespace Inventory.Domain.Identity;

/// <summary>Join entity assigning a <see cref="Role"/> to a <see cref="User"/>.</summary>
public sealed class UserRole : Entity
{
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }

    private UserRole() { }

    public UserRole(Guid userId, Guid roleId)
    {
        UserId = userId;
        RoleId = roleId;
    }
}
