using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Inventory.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;

namespace Inventory.Application.Features.Users;

public sealed class UserAccessAuthorization(
    IApplicationDbContext db, IUserAuthorizationProvider authorization, ICurrentUser currentUser)
{
    public async Task EnsureCanAssignAsync(Guid? targetUserId, IReadOnlyList<Guid> roleIds,
        IReadOnlyList<PermissionOverrideInput> overrides, bool hasAllWarehousesAccess,
        IReadOnlyList<Guid> warehouseIds, CancellationToken ct)
    {
        if (targetUserId == currentUser.UserId)
            throw new ForbiddenAccessException("You cannot change your own access.");

        var callerId = currentUser.UserId ?? throw new ForbiddenAccessException();
        var caller = await authorization.GetAuthorizationDataAsync(callerId, ct);
        var callerPermissions = PermissionEngine.ComputeEffectivePermissions(caller);

        var rolePermissionIds = await db.RolePermissions.AsNoTracking()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.PermissionId).Distinct().ToListAsync(ct);
        var allowIds = overrides.Where(o => o.Type == OverrideType.Allow)
            .Select(o => o.PermissionId).ToArray();
        var grantedIds = rolePermissionIds.Concat(allowIds).Distinct().ToArray();
        var grantedCodes = await db.Permissions.AsNoTracking()
            .Where(p => grantedIds.Contains(p.Id)).Select(p => p.Code).ToListAsync(ct);

        if (grantedCodes.Any(code => !callerPermissions.Contains(code)))
            throw new ForbiddenAccessException("You cannot grant permissions you do not have.");

        if (hasAllWarehousesAccess && !caller.HasAllWarehousesAccess)
            throw new ForbiddenAccessException("You cannot grant global warehouse access.");

        if (!caller.HasAllWarehousesAccess && warehouseIds.Any(id => !caller.WarehouseScopes.Contains(id)))
            throw new ForbiddenAccessException("You cannot grant access to a warehouse outside your scope.");
    }
}
