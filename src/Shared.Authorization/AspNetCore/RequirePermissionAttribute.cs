using Microsoft.AspNetCore.Authorization;

namespace Shared.Authorization.AspNetCore;

/// <summary>
/// Declarative permission check for controllers/actions, e.g. <c>[RequirePermission(Permissions.Products.Create)]</c>.
/// Resolves to a dynamic policy handled by <see cref="PermissionAuthorizationHandler"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";

    public RequirePermissionAttribute(string permission) => Policy = $"{PolicyPrefix}{permission}";

    public static bool TryGetPermission(string policyName, out string permission)
    {
        if (policyName.StartsWith(PolicyPrefix, StringComparison.Ordinal))
        {
            permission = policyName[PolicyPrefix.Length..];
            return true;
        }

        permission = string.Empty;
        return false;
    }
}
