using Inventory.Application.Features.Users;
using Inventory.Application.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Authorization;
using Shared.Authorization.AspNetCore;
using Shared.Contracts.Identity;

namespace Inventory.API.Controllers;

[Authorize]
[Route("api/v1/users")]
public sealed class UsersController : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> List([FromQuery] GetUsersPageQuery query, CancellationToken ct)
        => Ok((await Sender.Send(query, ct)).Items);

    [HttpGet("me")]
    public async Task<ActionResult<CurrentUserDto>> Me(CancellationToken ct)
        => Ok(await Sender.Send(new GetCurrentUserQuery(), ct));

    public sealed record ProfileRequest(string Email, string UserName, string FullName, bool IsActive);
    [HttpPut("{id:guid}")]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<IActionResult> Update(Guid id, ProfileRequest request, CancellationToken ct)
    {
        await Sender.Send(new UpdateUserCommand(id, request.Email, request.UserName, request.FullName, request.IsActive), ct);
        return NoContent();
    }

    [HttpGet("{id:guid}/access")]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<ActionResult<UpdateUserAccessCommand>> GetAccess(Guid id, CancellationToken ct)
        => Ok(await Sender.Send(new GetUserAccessQuery(id), ct));

    public sealed record AccessRequest(IReadOnlyList<Guid> RoleIds, IReadOnlyList<PermissionOverrideInput> Overrides,
        bool HasAllWarehousesAccess, IReadOnlyList<Guid> WarehouseIds);
    [HttpPut("{id:guid}/access")]
    [RequirePermission(Permissions.Users.Manage)]
    [RequirePermission(Permissions.Roles.Manage)]
    public async Task<IActionResult> Access(Guid id, AccessRequest request, CancellationToken ct)
    {
        await Sender.Send(new UpdateUserAccessCommand(id, request.RoleIds, request.Overrides,
            request.HasAllWarehousesAccess, request.WarehouseIds), ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/sessions")]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<IActionResult> RevokeSessions(Guid id, CancellationToken ct)
    {
        await Sender.Send(new RevokeSessionsCommand(id), ct);
        return NoContent();
    }

    [HttpPost]
    [RequirePermission(Permissions.Roles.Manage)]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<ActionResult<UserDto>> Create([FromBody] CreateUserCommand command, CancellationToken ct)
        => Ok(await Sender.Send(command, ct));
}

[Authorize]
[Route("api/v1/roles")]
public sealed class RolesController : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Roles.Manage)]
    public async Task<ActionResult<IReadOnlyList<RoleDto>>> List(CancellationToken ct)
        => Ok(await Sender.Send(new GetRolesQuery(), ct));
}

[Authorize]
[Route("api/v1/permissions")]
public sealed class PermissionsController : ApiControllerBase
{
    [HttpGet]
    [RequirePermission(Permissions.Roles.Manage)]
    public async Task<ActionResult<IReadOnlyList<PermissionCatalogItem>>> List(
        CancellationToken ct, [FromQuery] int offset = 0, [FromQuery] int limit = 100)
        => Ok(await Sender.Send(new GetPermissionCatalogQuery(offset, limit), ct));
}
