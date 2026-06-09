using Microsoft.AspNetCore.Authorization;

namespace Shared.Authorization.AspNetCore;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public string Permission { get; }

    public PermissionRequirement(string permission) => Permission = permission;
}
