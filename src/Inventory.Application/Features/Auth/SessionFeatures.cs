using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;

namespace Inventory.Application.Features.Auth;

public static class SessionRevocation
{
    public static async Task RevokeAsync(IApplicationDbContext db, Guid userId, DateTime now, CancellationToken ct)
    {
        var tokens = await db.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAtUtc == null).ToListAsync(ct);
        foreach (var token in tokens) token.Revoke(now);
    }
}

public sealed record RevokeSessionsCommand(Guid? UserId = null) : IRequest;
public sealed class RevokeSessionsHandler(IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock)
    : IRequestHandler<RevokeSessionsCommand>
{
    public async Task Handle(RevokeSessionsCommand request, CancellationToken ct)
    {
        var userId = request.UserId ?? currentUser.UserId ?? throw new ForbiddenAccessException();
        if (!await db.Users.AnyAsync(u => u.Id == userId, ct)) throw NotFoundException.For("User", userId);
        await SessionRevocation.RevokeAsync(db, userId, clock.UtcNow, ct);
        await db.SaveChangesAsync(ct);
    }
}

public sealed record CurrentUserDto(Guid Id, string Email, string UserName, string FullName,
    IReadOnlyList<string> Permissions, bool HasAllWarehousesAccess, IReadOnlyList<Guid> WarehouseIds);
public sealed record GetCurrentUserQuery : IRequest<CurrentUserDto>;
public sealed class GetCurrentUserHandler(IApplicationDbContext db, ICurrentUser currentUser, IUserAuthorizationProvider provider)
    : IRequestHandler<GetCurrentUserQuery, CurrentUserDto>
{
    public async Task<CurrentUserDto> Handle(GetCurrentUserQuery request, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().SingleOrDefaultAsync(u => u.Id == currentUser.UserId && u.IsActive, ct)
            ?? throw new ForbiddenAccessException();
        var data = await provider.GetAuthorizationDataAsync(user.Id, ct);
        return new(user.Id, user.Email, user.UserName, user.FullName,
            PermissionEngine.ComputeEffectivePermissions(data).Order().ToArray(),
            data.HasAllWarehousesAccess, data.WarehouseScopes.Order().ToArray());
    }
}
