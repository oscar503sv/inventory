using Inventory.Application.Common.Abstractions;
using Inventory.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;

namespace Inventory.Infrastructure.Authorization;

/// <summary>
/// Builds a user's <see cref="UserAuthorizationData"/> from PostgreSQL (ADR-005 source of truth).
/// In Phase 2 this is fronted by a Redis permission cache.
/// </summary>
public sealed class UserAuthorizationProvider : IUserAuthorizationProvider
{
    private readonly IApplicationDbContext _db;

    public UserAuthorizationProvider(IApplicationDbContext db) => _db = db;

    public async Task<UserAuthorizationData> GetAuthorizationDataAsync(
        Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .AsNoTracking()
            .Include(u => u.Roles)
            .Include(u => u.Overrides)
            .Include(u => u.Scopes)
            .FirstOrDefaultAsync(u => u.Id == userId, cancellationToken);

        if (user is null || !user.IsActive)
            return UserAuthorizationData.Empty;

        var roleIds = user.Roles.Select(r => r.RoleId).ToList();

        var rolePermissionIds = await _db.RolePermissions
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.PermissionId)
            .Distinct()
            .ToListAsync(cancellationToken);

        var allowIds = user.Overrides
            .Where(o => o.OverrideType == OverrideType.Allow).Select(o => o.PermissionId).ToList();
        var denyIds = user.Overrides
            .Where(o => o.OverrideType == OverrideType.Deny).Select(o => o.PermissionId).ToList();

        var neededIds = rolePermissionIds.Concat(allowIds).Concat(denyIds).Distinct().ToList();
        var codesById = await _db.Permissions
            .Where(p => neededIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => p.Code, cancellationToken);

        string[] Codes(IEnumerable<Guid> ids) =>
            ids.Where(codesById.ContainsKey).Select(id => codesById[id]).ToArray();

        return new UserAuthorizationData(
            RolePermissions: new HashSet<string>(Codes(rolePermissionIds), StringComparer.OrdinalIgnoreCase),
            AllowOverrides: new HashSet<string>(Codes(allowIds), StringComparer.OrdinalIgnoreCase),
            DenyOverrides: new HashSet<string>(Codes(denyIds), StringComparer.OrdinalIgnoreCase),
            HasAllWarehousesAccess: user.HasAllWarehousesAccess,
            WarehouseScopes: new HashSet<Guid>(user.Scopes.Select(s => s.WarehouseId)));
    }
}
