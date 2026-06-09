using Inventory.Application.Features.Users;
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
    public async Task<ActionResult<IReadOnlyList<UserDto>>> List(CancellationToken ct)
        => Ok(await Sender.Send(new GetUsersQuery(), ct));

    [HttpPost]
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
