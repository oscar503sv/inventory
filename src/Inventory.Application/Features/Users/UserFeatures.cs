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

    public CreateUserCommandHandler(IApplicationDbContext db, IPasswordHasher passwordHasher)
    {
        _db = db;
        _passwordHasher = passwordHasher;
    }

    public async Task<UserDto> Handle(CreateUserCommand request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        if (await _db.Users.AnyAsync(u => u.Email == email, cancellationToken))
            throw new ConflictException($"A user with email '{email}' already exists.");

        var roleIds = request.RoleIds?.Distinct().ToList() ?? [];
        var roles = await _db.Roles.Where(r => roleIds.Contains(r.Id)).ToListAsync(cancellationToken);
        if (roles.Count != roleIds.Count)
            throw new NotFoundException("One or more roles were not found.");

        var user = User.Create(
            request.Email, request.UserName, request.FullName,
            _passwordHasher.Hash(request.Password), request.HasAllWarehousesAccess);

        foreach (var role in roles)
            user.AssignRole(role.Id);

        if (!request.HasAllWarehousesAccess && request.WarehouseScopeIds is { Count: > 0 })
        {
            var scopeIds = request.WarehouseScopeIds.Distinct().ToList();
            var validWarehouses = await _db.Warehouses
                .Where(w => scopeIds.Contains(w.Id)).Select(w => w.Id).ToListAsync(cancellationToken);
            foreach (var warehouseId in validWarehouses)
                user.GrantWarehouseScope(warehouseId);
        }

        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        return new UserDto(
            user.Id, user.Email, user.UserName, user.FullName, user.IsActive,
            user.HasAllWarehousesAccess, roles.Select(r => r.Name).ToList());
    }
}

public sealed record GetUsersQuery : IRequest<IReadOnlyList<UserDto>>;

public sealed class GetUsersQueryHandler : IRequestHandler<GetUsersQuery, IReadOnlyList<UserDto>>
{
    private readonly IApplicationDbContext _db;
    public GetUsersQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<UserDto>> Handle(GetUsersQuery request, CancellationToken cancellationToken)
    {
        var users = await _db.Users.AsNoTracking().Include(u => u.Roles)
            .OrderBy(u => u.Email).ToListAsync(cancellationToken);

        var roleNames = await _db.Roles.AsNoTracking()
            .ToDictionaryAsync(r => r.Id, r => r.Name, cancellationToken);

        return users.Select(u => new UserDto(
            u.Id, u.Email, u.UserName, u.FullName, u.IsActive, u.HasAllWarehousesAccess,
            u.Roles.Select(r => roleNames.GetValueOrDefault(r.RoleId, "?")).ToList())).ToList();
    }
}
