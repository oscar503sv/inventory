using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Inventory.Domain.Identity;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Identity;

namespace Inventory.Application.Features.Users;

public sealed record CreateUserCommand(
    string Email,
    string UserName,
    string FullName,
    string Password,
    IReadOnlyList<Guid> RoleIds,
    bool HasAllWarehousesAccess,
    IReadOnlyList<Guid> WarehouseScopeIds) : IRequest<UserDto>;

public sealed class CreateUserCommandValidator : AbstractValidator<CreateUserCommand>
{
    public CreateUserCommandValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.UserName).NotEmpty().MaximumLength(128);
        RuleFor(x => x.Password).NotEmpty().MinimumLength(8);
    }
}

public sealed class CreateUserCommandHandler : IRequestHandler<CreateUserCommand, UserDto>
{
    private readonly IApplicationDbContext _db;
    private readonly IPasswordHasher _passwordHasher;
    private readonly UserAccessAuthorization _authorization;

    public CreateUserCommandHandler(IApplicationDbContext db, IPasswordHasher passwordHasher,
        UserAccessAuthorization authorization)
    {
        _db = db;
        _passwordHasher = passwordHasher;
        _authorization = authorization;
    }

    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == email, cancellationToken))
            throw new ConflictException($"A user with email '{email}' already exists.");

        var roleIds = request.RoleIds?.Distinct().ToList() ?? [];
        await UserAccessReferences.ValidateAsync(_db, roleIds, request.WarehouseScopeIds ?? [], [], cancellationToken);
        await _authorization.EnsureCanAssignAsync(null, roleIds, [], request.HasAllWarehousesAccess,
            request.WarehouseScopeIds ?? [], cancellationToken);
        var roles = await _db.Roles.Where(r => roleIds.Contains(r.Id)).ToListAsync(cancellationToken);

        var user = User.Create(
            request.Email, request.UserName, request.FullName,
            _passwordHasher.Hash(request.Password), request.HasAllWarehousesAccess);

        foreach (var role in roles)
            user.AssignRole(role.Id);

        if (request.WarehouseScopeIds is { Count: > 0 })
        {
            var scopeIds = request.WarehouseScopeIds.Distinct().ToList();
            foreach (var warehouseId in scopeIds)
                user.GrantWarehouseScope(warehouseId);
        }

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        return new UserDto(
            user.Id, user.Email, user.UserName, user.FullName, user.IsActive,
            user.HasAllWarehousesAccess, roles.Select(r => r.Name).ToList());
    }
}
