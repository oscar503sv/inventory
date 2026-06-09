namespace Inventory.Domain.Identity;

/// <summary>Direction of a per-user permission override (ADR-003).</summary>
public enum OverrideType
{
    Allow = 1,
    Deny = 2
}
