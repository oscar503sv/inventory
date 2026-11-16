using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Queries;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Common;
using Shared.Contracts.Identity;

namespace Inventory.Application.Features.Users;

public sealed record GetUsersPageQuery(int Page = 1, int PageSize = QueryPaging.DefaultPageSize)
    : IRequest<PagedResult<UserDto>>, IPagedQuery;
public sealed class GetUsersPageQueryValidator : PagedQueryValidator<GetUsersPageQuery>;

public sealed class GetUsersPageQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetUsersPageQuery, PagedResult<UserDto>>
{
    public async Task<PagedResult<UserDto>> Handle(GetUsersPageQuery request, CancellationToken ct)
    {
        var query = db.Users.AsNoTracking().OrderBy(u => u.Email).ThenBy(u => u.Id)
            .Select(u => new UserDto(u.Id, u.Email, u.UserName, u.FullName, u.IsActive,
                u.HasAllWarehousesAccess,
                (from assignment in u.Roles
                 join role in db.Roles on assignment.RoleId equals role.Id
                 orderby role.Name
                 select role.Name).ToList()));
        return await query.ToPageAsync(request.Page, request.PageSize, ct);
    }
}

public sealed record GetPermissionCatalogPageQuery(int Page = 1, int PageSize = QueryPaging.DefaultPageSize)
    : IRequest<PagedResult<PermissionCatalogItem>>, IPagedQuery;
public sealed class GetPermissionCatalogPageQueryValidator : PagedQueryValidator<GetPermissionCatalogPageQuery>;

public sealed class GetPermissionCatalogPageQueryHandler(IApplicationDbContext db)
    : IRequestHandler<GetPermissionCatalogPageQuery, PagedResult<PermissionCatalogItem>>
{
    public async Task<PagedResult<PermissionCatalogItem>> Handle(GetPermissionCatalogPageQuery request, CancellationToken ct)
        => await db.Permissions.AsNoTracking().OrderBy(p => p.Code).ThenBy(p => p.Id)
            .Select(p => new PermissionCatalogItem(p.Id, p.Code, p.Description))
            .ToPageAsync(request.Page, request.PageSize, ct);
}
