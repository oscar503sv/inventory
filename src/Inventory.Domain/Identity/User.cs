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
        _overrides.RemoveAll(o => o.PermissionId == permissionId);
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

    public void ChangePassword(string newPasswordHash)
    {
        if (string.IsNullOrWhiteSpace(newPasswordHash))
            throw new DomainException("Password hash is required.");

        PasswordHash = newPasswordHash;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
