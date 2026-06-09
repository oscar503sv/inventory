using Inventory.Application.Common.Abstractions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Identity;

namespace Inventory.Application.Features.Users;

public sealed record GetRolesQuery : IRequest<IReadOnlyList<RoleDto>>;

public sealed class GetRolesQueryHandler : IRequestHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    private readonly IApplicationDbContext _db;
    public GetRolesQueryHandler(IApplicationDbContext db) => _db = db;

    public async Task<IReadOnlyList<RoleDto>> Handle(GetRolesQuery request, CancellationToken cancellationToken)
    {
        var roles = await _db.Roles.AsNoTracking().Include(r => r.Permissions)
            .OrderBy(r => r.Name).ToListAsync(cancellationToken);

        var permissionCodes = await _db.Permissions.AsNoTracking()
            .ToDictionaryAsync(p => p.Id, p => p.Code, cancellationToken);

        return roles.Select(r => new RoleDto(
            r.Id, r.Name, r.Description,
            r.Permissions.Select(p => permissionCodes.GetValueOrDefault(p.PermissionId, "?"))
                .OrderBy(c => c).ToList())).ToList();
    }
}
