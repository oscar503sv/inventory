using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Inventory.Application.Common.Validation;
using Inventory.Application.Features.Auth;
using Inventory.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Authorization;

namespace Inventory.Application.Features.Users;

public sealed record UpdateUserCommand(Guid UserId, string Email, string UserName, string FullName, bool IsActive) : IRequest, IUserProfileInput;

public sealed class UpdateUserValidator : AbstractValidator<UpdateUserCommand>
{
    public UpdateUserValidator()
    {
        Include(new UserProfileValidator<UpdateUserCommand>());
    }
}

public sealed class UpdateUserHandler(IApplicationDbContext db, IDateTimeProvider clock,
    ICurrentUser currentUser, IUserAuthorizationProvider authorization) : IRequestHandler<UpdateUserCommand>
{
    public async Task Handle(UpdateUserCommand request, CancellationToken ct)
    {
        await db.ExecuteAdministratorUpdateAsync(async () =>
        {
            var user = await db.Users.Include(u => u.Roles).Include(u => u.Overrides).Include(u => u.Scopes)
                .SingleOrDefaultAsync(u => u.Id == request.UserId, ct)
                ?? throw NotFoundException.For("User", request.UserId);
            await AdministratorUpdateGuard.EnsureActorAuthorizedAsync(authorization, currentUser, Permissions.Users.Manage, ct);
            var email = request.Email.Trim().ToLowerInvariant();
            if (currentUser.UserId == user.Id && (!request.IsActive || email != user.Email))
                throw new ForbiddenAccessException("You cannot deactivate yourself or change your own email.");
            var administratorRoleId = await AdministratorUpdateGuard.GetRoleIdAsync(db, ct);
            var isAdministrator = await db.UserRoles.AnyAsync(r => r.UserId == user.Id && r.RoleId == administratorRoleId, ct);
            if (isAdministrator)
                await AdministratorUpdateGuard.EnsureWithinCeilingAsync(db, authorization, currentUser, user.Id,
                    user.Roles.Select(r => r.RoleId), user.Overrides.Select(o => new PermissionOverrideInput(o.PermissionId, o.OverrideType)),
                    user.HasAllWarehousesAccess, user.Scopes.Select(s => s.WarehouseId), Permissions.Users.Manage, ct);
            if (isAdministrator && user.IsActive && !request.IsActive)
                await AdministratorUpdateGuard.EnsureReplacementAsync(db, user.Id, administratorRoleId, ct);
            if (await db.Users.AnyAsync(u => u.Id != user.Id && u.Email == email, ct))
                throw new ConflictException("Email is already in use.");
            user.UpdateProfile(email, request.UserName, request.FullName);
            if (request.IsActive) user.Activate();
            else
            {
                user.Deactivate();
                await SessionRevocation.RevokeAsync(db, user.Id, clock.UtcNow, ct);
            }
            await db.SaveChangesAsync(ct);
        }, ct);
    }
}

public sealed record PermissionOverrideInput(Guid PermissionId, OverrideType Type);
public sealed record UpdateUserAccessCommand(Guid UserId, IReadOnlyList<Guid> RoleIds,
    IReadOnlyList<PermissionOverrideInput> Overrides, bool HasAllWarehousesAccess,
    IReadOnlyList<Guid> WarehouseIds) : IRequest;

public sealed class UpdateUserAccessValidator : AbstractValidator<UpdateUserAccessCommand>
{
    public UpdateUserAccessValidator()
    {
        RuleFor(x => x.RoleIds).NotNull();
        Include(new WarehouseScopeValidator<UpdateUserAccessCommand>(x => x.WarehouseIds, x => x.HasAllWarehousesAccess));
        RuleFor(x => x.Overrides).NotNull();
        RuleForEach(x => x.Overrides).NotNull().ChildRules(o =>
        {
            o.RuleFor(x => x.Type).IsInEnum();
            o.RuleFor(x => x.PermissionId).NotEmpty();
        });
        RuleFor(x => x.Overrides).Must(x => x is null || x.All(o => o is not null) && x.Select(o => o.PermissionId).Distinct().Count() == x.Count)
            .WithMessage("Permission overrides must be unique.");
    }
}

public sealed class UpdateUserAccessHandler(IApplicationDbContext db, ICurrentUser currentUser,
    IUserAuthorizationProvider authorization, UserAccessAuthorization accessAuthorization) : IRequestHandler<UpdateUserAccessCommand>
{
    public async Task Handle(UpdateUserAccessCommand request, CancellationToken ct)
    {
        await db.ExecuteAdministratorUpdateAsync(async () =>
        {
            var user = await db.Users.Include(u => u.Roles).Include(u => u.Overrides).Include(u => u.Scopes)
                .SingleOrDefaultAsync(u => u.Id == request.UserId, ct)
                ?? throw NotFoundException.For("User", request.UserId);
            if (currentUser.UserId == user.Id)
                throw new ForbiddenAccessException("You cannot change your own access.");
            await AdministratorUpdateGuard.EnsureActorAuthorizedAsync(authorization, currentUser, Permissions.Roles.Manage, ct);
            var administratorRoleId = await AdministratorUpdateGuard.GetRoleIdAsync(db, ct);
            var isAdministrator = user.Roles.Any(r => r.RoleId == administratorRoleId);
            if (isAdministrator)
            {
                await AdministratorUpdateGuard.EnsureWithinCeilingAsync(db, authorization, currentUser, user.Id,
                    user.Roles.Select(r => r.RoleId), user.Overrides.Select(o => new PermissionOverrideInput(o.PermissionId, o.OverrideType)),
                    user.HasAllWarehousesAccess, user.Scopes.Select(s => s.WarehouseId), Permissions.Roles.Manage, ct);
                await AdministratorUpdateGuard.EnsureWithinCeilingAsync(db, authorization, currentUser, user.Id,
                    request.RoleIds, request.Overrides, request.HasAllWarehousesAccess, request.WarehouseIds,
                    Permissions.Roles.Manage, ct);
                if (user.IsActive && !request.RoleIds.Contains(administratorRoleId))
                    await AdministratorUpdateGuard.EnsureReplacementAsync(db, user.Id, administratorRoleId, ct);
            }
            await UserAccessReferences.ValidateAsync(db, request.RoleIds, request.WarehouseIds,
                request.Overrides.Select(o => o.PermissionId).ToArray(), ct);
            await accessAuthorization.EnsureCanAssignAsync(request.UserId, request.RoleIds, request.Overrides,
                request.HasAllWarehousesAccess, request.WarehouseIds, ct);
            foreach (var role in user.Roles.ToArray())
                if (!request.RoleIds.Contains(role.RoleId)) user.RemoveRole(role.RoleId);
            foreach (var id in request.RoleIds.Distinct())
            {
                if (user.Roles.Any(r => r.RoleId == id)) continue;
                user.AssignRole(id);
                db.UserRoles.Add(user.Roles.Single(r => r.RoleId == id));
            }
            foreach (var item in user.Overrides.ToArray())
                if (!request.Overrides.Any(o => o.PermissionId == item.PermissionId)) user.RemoveOverride(item.PermissionId);
            foreach (var item in request.Overrides)
            {
                var exists = user.Overrides.Any(o => o.PermissionId == item.PermissionId);
                user.SetOverride(item.PermissionId, item.Type);
                if (!exists) db.UserPermissionOverrides.Add(user.Overrides.Single(o => o.PermissionId == item.PermissionId));
            }
            UpdateScopes(user, request);
            await db.SaveChangesAsync(ct);
        }, ct);
    }

    private void UpdateScopes(User user, UpdateUserAccessCommand request)
    {
        if (request.HasAllWarehousesAccess) { user.GrantAllWarehousesAccess(); return; }
        user.RevokeAllWarehousesAccess();
        foreach (var scope in user.Scopes.ToArray())
            if (!request.WarehouseIds.Contains(scope.WarehouseId)) user.RevokeWarehouseScope(scope.WarehouseId);
        foreach (var id in request.WarehouseIds.Distinct())
        {
            if (user.Scopes.Any(s => s.WarehouseId == id)) continue;
            user.GrantWarehouseScope(id);
            db.WarehouseScopes.Add(user.Scopes.Single(s => s.WarehouseId == id));
        }
    }
}

public sealed record GetUserAccessQuery(Guid UserId) : IRequest<UpdateUserAccessCommand>;
public sealed class GetUserAccessHandler(IApplicationDbContext db) : IRequestHandler<GetUserAccessQuery, UpdateUserAccessCommand>
{
    public async Task<UpdateUserAccessCommand> Handle(GetUserAccessQuery request, CancellationToken ct)
    {
        var user = await db.Users.AsNoTracking().Include(u => u.Roles).Include(u => u.Overrides).Include(u => u.Scopes)
            .SingleOrDefaultAsync(u => u.Id == request.UserId, ct) ?? throw NotFoundException.For("User", request.UserId);
        return new(user.Id, user.Roles.Select(r => r.RoleId).ToArray(),
            user.Overrides.Select(o => new PermissionOverrideInput(o.PermissionId, o.OverrideType)).ToArray(),
            user.HasAllWarehousesAccess, user.Scopes.Select(s => s.WarehouseId).ToArray());
    }
}

public sealed record PermissionCatalogItem(Guid Id, string Code, string Description);
public sealed record GetPermissionCatalogQuery(int Offset = 0, int Limit = 100) : IRequest<IReadOnlyList<PermissionCatalogItem>>;
public sealed class GetPermissionCatalogHandler(IApplicationDbContext db)
    : IRequestHandler<GetPermissionCatalogQuery, IReadOnlyList<PermissionCatalogItem>>
{
    public async Task<IReadOnlyList<PermissionCatalogItem>> Handle(GetPermissionCatalogQuery request, CancellationToken ct)
        => await db.Permissions.AsNoTracking().OrderBy(p => p.Code).ThenBy(p => p.Id).Skip(Math.Max(0, request.Offset))
            .Take(Math.Clamp(request.Limit, 1, 100)).Select(p => new PermissionCatalogItem(p.Id, p.Code, p.Description)).ToListAsync(ct);
}
