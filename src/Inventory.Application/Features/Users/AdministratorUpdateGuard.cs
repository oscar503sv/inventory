using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Inventory.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;

namespace Inventory.Application.Features.Users;

internal static class AdministratorUpdateGuard
{
    public static async Task EnsureActorAuthorizedAsync(IUserAuthorizationProvider authorization,
        ICurrentUser currentUser, string requiredPermission, CancellationToken ct)
    {
        var actorId = currentUser.UserId ?? throw new ForbiddenAccessException();
        // This entry point runs after the administrator lock; the HTTP policy snapshot may be stale.
        authorization.Invalidate(actorId);
        var actor = await authorization.GetAuthorizationDataAsync(actorId, ct);
        var effective = PermissionEngine.ComputeEffectivePermissions(actor);
        if (!effective.Contains(Permissions.Users.Manage) || !effective.Contains(requiredPermission))
            throw new ForbiddenAccessException();
    }

    public static async Task<Guid> GetRoleIdAsync(IApplicationDbContext db, CancellationToken ct)
        => await db.Roles.Where(r => r.Name == RoleDefinitions.Administrator)
            .Select(r => r.Id).SingleAsync(ct);

    public static async Task EnsureReplacementAsync(IApplicationDbContext db, Guid targetId,
        Guid administratorRoleId, CancellationToken ct)
    {
        var hasReplacement = await db.Users.AnyAsync(u => u.Id != targetId && u.IsActive &&
            db.UserRoles.Any(r => r.UserId == u.Id && r.RoleId == administratorRoleId), ct);
        if (!hasReplacement)
            throw new ConflictException("The last active administrator cannot be removed or deactivated.");
    }

    public static async Task EnsureWithinCeilingAsync(IApplicationDbContext db,
        IUserAuthorizationProvider authorization, ICurrentUser currentUser, Guid targetId,
        IEnumerable<Guid> roleIds, IEnumerable<PermissionOverrideInput> overrides,
        bool hasAllWarehousesAccess, IEnumerable<Guid> warehouseIds, string requiredPermission, CancellationToken ct)
    {
        var actorId = currentUser.UserId ?? throw new ForbiddenAccessException();
        var actor = await authorization.GetAuthorizationDataAsync(actorId, ct);
        var effective = PermissionEngine.ComputeEffectivePermissions(actor);
        if (!effective.Contains(Permissions.Users.Manage) || !effective.Contains(requiredPermission))
            throw new ForbiddenAccessException();

        var roleIdSet = roleIds.ToHashSet();
        var grantedIds = await db.RolePermissions.Where(r => roleIdSet.Contains(r.RoleId))
            .Select(r => r.PermissionId).ToListAsync(ct);
        grantedIds.AddRange(overrides.Where(o => o.Type == OverrideType.Allow).Select(o => o.PermissionId));
        var codes = await db.Permissions.Where(p => grantedIds.Contains(p.Id)).Select(p => p.Code).ToListAsync(ct);
        if (codes.Any(code => !effective.Contains(code)) ||
            (hasAllWarehousesAccess && !actor.HasAllWarehousesAccess) ||
            (!actor.HasAllWarehousesAccess && warehouseIds.Any(id => !actor.WarehouseScopes.Contains(id))))
            throw new ForbiddenAccessException($"You cannot edit administrator '{targetId}' beyond your access.");
    }
}
