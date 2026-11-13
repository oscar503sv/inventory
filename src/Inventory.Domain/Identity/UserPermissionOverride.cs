using Inventory.Domain.Common;

namespace Inventory.Domain.Identity;

/// <summary>
/// A per-user Allow/Deny override for a single permission. Deny always wins over Allow and over
/// role-granted permissions (ADR-003).
/// </summary>
public sealed class UserPermissionOverride : Entity
{
    public Guid UserId { get; private set; }
    public Guid PermissionId { get; private set; }
    public OverrideType OverrideType { get; private set; }

    public void ChangeType(OverrideType overrideType)
    {
        if (!Enum.IsDefined(overrideType))
            throw new DomainException("Invalid permission override type.");
        OverrideType = overrideType;
    }

    private UserPermissionOverride() { }

    public UserPermissionOverride(Guid userId, Guid permissionId, OverrideType overrideType)
    {
        UserId = userId;
        PermissionId = permissionId;
        OverrideType = overrideType;
    }
}
