using Inventory.Application.Features.Users;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Authorization;
using Shared.Authorization.AspNetCore;
using Shared.Contracts.Common;
using Shared.Contracts.Identity;

namespace Inventory.API.Controllers;

[Authorize]
[Route("api/v2")]
[ApiExplorerSettings(GroupName = "v2")]
public sealed class UserQueriesController : ApiControllerBase
{
    [HttpGet("users")]
    [RequirePermission(Permissions.Users.Manage)]
    public async Task<ActionResult<PagedResult<UserDto>>> Users([FromQuery] GetUsersPageQuery query, CancellationToken ct)
        => Ok(await Sender.Send(query, ct));

    [HttpGet("permissions")]
    [RequirePermission(Permissions.Roles.Manage)]
    public async Task<ActionResult<PagedResult<PermissionCatalogItem>>> PermissionsCatalog(
        [FromQuery] GetPermissionCatalogPageQuery query, CancellationToken ct)
        => Ok(await Sender.Send(query, ct));
}
