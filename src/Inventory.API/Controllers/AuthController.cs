using Inventory.Application.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Shared.Contracts.Identity;

namespace Inventory.API.Controllers;

[AllowAnonymous]
[Route("api/v1/auth")]
public sealed class AuthController : ApiControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<AuthResultDto>> Login([FromBody] LoginCommand command, CancellationToken ct)
        => Ok(await Sender.Send(command, ct));

    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResultDto>> Refresh([FromBody] RefreshTokenCommand command, CancellationToken ct)
        => Ok(await Sender.Send(command, ct));
}
