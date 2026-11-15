using Inventory.Domain.Common;
using Inventory.Domain.Identity.Events;

namespace Inventory.Domain.Identity;

/// <summary>
/// A system user. Authorization is derived from assigned roles, per-user overrides and warehouse
/// scopes (ADR-003). Effective permissions are computed by the authorization engine, not stored here.
/// </summary>
public sealed class User : AggregateRoot
{
    private readonly List<UserRole> _roles = new();
    private readonly List<UserPermissionOverride> _overrides = new();
    private readonly List<WarehouseScope> _scopes = new();

    public string Email { get; private set; } = null!;
    public string UserName { get; private set; } = null!;
    public string FullName { get; private set; } = null!;
    public string PasswordHash { get; private set; } = null!;
    public bool IsActive { get; private set; }

    /// <summary>When true, the user is authorized on every warehouse and <see cref="Scopes"/> is empty.</summary>
    public bool HasAllWarehousesAccess { get; private set; }

    public IReadOnlyCollection<UserRole> Roles => _roles.AsReadOnly();
    public IReadOnlyCollection<UserPermissionOverride> Overrides => _overrides.AsReadOnly();
    public IReadOnlyCollection<WarehouseScope> Scopes => _scopes.AsReadOnly();

    private User() { }

    public static User Create(
        string email,
        string userName,
        string fullName,
        string passwordHash,
        bool hasAllWarehousesAccess = false)
    {
        if (string.IsNullOrWhiteSpace(email))
            throw new DomainException("Email is required.");
        if (string.IsNullOrWhiteSpace(userName))
            throw new DomainException("User name is required.");
        if (string.IsNullOrWhiteSpace(passwordHash))
            throw new DomainException("Password hash is required.");

        var user = new User
        {
            Email = email.Trim().ToLowerInvariant(),
            UserName = userName.Trim(),
            FullName = fullName?.Trim() ?? string.Empty,
            PasswordHash = passwordHash,
            IsActive = true,
            HasAllWarehousesAccess = hasAllWarehousesAccess
        };

        user.Raise(new UserCreatedDomainEvent(user.Id, user.Email));
        return user;
    }

    public void AssignRole(Guid roleId)
    {
        if (_roles.Any(r => r.RoleId == roleId))
            return;

        _roles.Add(new UserRole(Id, roleId));
        Raise(new PermissionChangedDomainEvent(Id));
    }

    public void SetOverride(Guid permissionId, OverrideType overrideType)
    {
        if (!Enum.IsDefined(overrideType))
            throw new DomainException("Invalid permission override type.");
        var existing = _overrides.SingleOrDefault(o => o.PermissionId == permissionId);
        if (existing is not null)
        {
            existing.ChangeType(overrideType);
            Raise(new PermissionChangedDomainEvent(Id));
            return;
        }
        _overrides.Add(new UserPermissionOverride(Id, permissionId, overrideType));
        Raise(new PermissionChangedDomainEvent(Id));
    }

    public void GrantWarehouseScope(Guid warehouseId)
    {
        if (HasAllWarehousesAccess || _scopes.Any(s => s.WarehouseId == warehouseId))
            return;

        _scopes.Add(new WarehouseScope(Id, warehouseId));
        Raise(new PermissionChangedDomainEvent(Id));
    }

    public void GrantAllWarehousesAccess()
    {
        HasAllWarehousesAccess = true;
        _scopes.Clear();
        Raise(new PermissionChangedDomainEvent(Id));
    }

    public void RemoveRole(Guid roleId)
    {
        _roles.RemoveAll(r => r.RoleId == roleId);
        Raise(new PermissionChangedDomainEvent(Id));
    }

    public void RemoveOverride(Guid permissionId)
    {
        _overrides.RemoveAll(o => o.PermissionId == permissionId);
        Raise(new PermissionChangedDomainEvent(Id));
    }

    public void RevokeWarehouseScope(Guid warehouseId)
    {
        _scopes.RemoveAll(s => s.WarehouseId == warehouseId);
        Raise(new PermissionChangedDomainEvent(Id));
    }

    public void RevokeAllWarehousesAccess()
    {
        HasAllWarehousesAccess = false;
        Raise(new PermissionChangedDomainEvent(Id));
    }

    public void UpdateProfile(string email, string userName, string fullName)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(userName))
            throw new DomainException("Email and user name are required.");
        Email = email.Trim().ToLowerInvariant();
        UserName = userName.Trim();
        FullName = fullName.Trim();
    }

    public void ChangePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            throw new DomainException("Password hash is required.");

        PasswordHash = newPasswordHash;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
