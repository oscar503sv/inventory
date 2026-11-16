using Inventory.Application.Common.Abstractions;
using Inventory.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;

namespace Inventory.Infrastructure.Authorization;

/// <summary>Loads authorization with bounded projections, avoiding joins between independent collections.</summary>
public sealed class UserAuthorizationProvider(IApplicationDbContext db) : IUserAuthorizationProvider
{
    public async Task<UserAuthorizationData> GetAuthorizationDataAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var user = await db.Users.AsNoTracking().TagWith("Authorization.User").Where(u => u.Id == userId)
            .Select(u => new { u.IsActive, u.HasAllWarehousesAccess }).SingleOrDefaultAsync(cancellationToken);
        if (user is null || !user.IsActive) return UserAuthorizationData.Empty;

        var roleCodes = await (from assignment in db.UserRoles.AsNoTracking().TagWith("Authorization.Roles")
                               join rolePermission in db.RolePermissions on assignment.RoleId equals rolePermission.RoleId
                               join permission in db.Permissions on rolePermission.PermissionId equals permission.Id
                               where assignment.UserId == userId
                               select permission.Code).Distinct().ToListAsync(cancellationToken);
        var overrides = await (from item in db.UserPermissionOverrides.AsNoTracking().TagWith("Authorization.Overrides")
                               join permission in db.Permissions on item.PermissionId equals permission.Id
                               where item.UserId == userId
                               select new { permission.Code, item.OverrideType }).ToListAsync(cancellationToken);
        var scopes = await db.WarehouseScopes.AsNoTracking().TagWith("Authorization.Scopes").Where(s => s.UserId == userId)
            .Select(s => s.WarehouseId).ToListAsync(cancellationToken);

        return new UserAuthorizationData(
            new HashSet<string>(roleCodes, StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(overrides.Where(o => o.OverrideType == OverrideType.Allow).Select(o => o.Code), StringComparer.OrdinalIgnoreCase),
            new HashSet<string>(overrides.Where(o => o.OverrideType == OverrideType.Deny).Select(o => o.Code), StringComparer.OrdinalIgnoreCase),
            user.HasAllWarehousesAccess, new HashSet<Guid>(scopes));
    }
}
