using Inventory.Application.Common.Abstractions;
using Inventory.Application.Features.Auth;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace Inventory.API.RateLimiting;

// Model binding supplies the commands; the request body is never parsed a second time.
public sealed class AuthAccountRateLimitFilter(AuthRateLimits limits, IApplicationDbContext db,
    IRefreshTokenService tokens) : IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var command = context.ActionArguments.Values.FirstOrDefault(v => v is LoginCommand or RefreshTokenCommand);
        string? email = command is LoginCommand login ? login.Email : null;
        if (command is RefreshTokenCommand refresh && !string.IsNullOrWhiteSpace(refresh.RefreshToken))
        {
            var hash = tokens.Hash(refresh.RefreshToken);
            email = await (from token in db.RefreshTokens.AsNoTracking()
                           join user in db.Users.AsNoTracking() on token.UserId equals user.Id
                           where token.TokenHash == hash
                           select user.Email).FirstOrDefaultAsync(context.HttpContext.RequestAborted);
        }

        if (!string.IsNullOrWhiteSpace(email))
        {
            using var lease = limits.AcquireAccount(email);
            if (!lease.IsAcquired)
            {
                await AuthRateLimits.RejectAsync(context.HttpContext, lease);
                return;
            }
        }
        await next();
    }
}
