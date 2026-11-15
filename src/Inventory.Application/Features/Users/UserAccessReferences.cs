using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Features.Users;

internal static class UserAccessReferences
{
    public static async Task ValidateAsync(IApplicationDbContext db, IReadOnlyList<Guid> roles,
        IReadOnlyList<Guid> warehouses, IReadOnlyList<Guid> permissions, CancellationToken ct)
    {
        if (await db.Roles.CountAsync(r => roles.Contains(r.Id), ct) != roles.Distinct().Count())
            throw new NotFoundException("One or more roles were not found.");
        if (await db.Warehouses.CountAsync(w => warehouses.Contains(w.Id), ct) != warehouses.Distinct().Count())
            throw new NotFoundException("One or more warehouses were not found.");
        if (await db.Permissions.CountAsync(p => permissions.Contains(p.Id), ct) != permissions.Distinct().Count())
            throw new NotFoundException("One or more permissions were not found.");
    }
}
