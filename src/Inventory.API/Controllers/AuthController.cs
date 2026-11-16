using Inventory.API.RateLimiting;
using Inventory.Application.Features.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Shared.Contracts.Identity;

namespace Inventory.API.Controllers;

[Route("api/v1/auth")]
public sealed class AuthController : ApiControllerBase
{
    [Authorize]
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        await Sender.Send(new RevokeSessionsCommand(), ct);
        return NoContent();
    }

    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimits.PolicyName)]
    [ServiceFilter(typeof(AuthAccountRateLimitFilter))]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResultDto>> Login([FromBody] LoginCommand command, CancellationToken ct)
        => Ok(await Sender.Send(command, ct));

    [AllowAnonymous]
    [EnableRateLimiting(AuthRateLimits.PolicyName)]
    [ServiceFilter(typeof(AuthAccountRateLimitFilter))]
    [HttpPost("refresh")]
    public async Task<ActionResult<AuthResultDto>> Refresh([FromBody] RefreshTokenCommand command, CancellationToken ct)
        => Ok(await Sender.Send(command, ct));
}
