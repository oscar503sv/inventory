using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Shared.Authorization.AspNetCore;

/// <summary>
/// Resolves the caller's effective permissions (ADR-003) and authorizes the requirement.
/// Warehouse-scope checks are contextual and enforced in the application handlers where the target
/// warehouse is known.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IUserAuthorizationProvider _provider;

    public PermissionAuthorizationHandler(IUserAuthorizationProvider provider) => _provider = provider;

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context, PermissionRequirement requirement)
    {
        var userIdValue = context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? context.User.FindFirst("sub")?.Value;

        if (!Guid.TryParse(userIdValue, out var userId))
            return;

        var data = await _provider.GetAuthorizationDataAsync(userId);

        if (PermissionEngine.HasPermission(data, requirement.Permission))
            context.Succeed(requirement);
    }
}
