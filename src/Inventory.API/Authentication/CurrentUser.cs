using System.Security.Claims;
using Inventory.Application.Common.Abstractions;

namespace Inventory.API.Authentication;

/// <summary>Resolves the caller from the current <see cref="HttpContext"/> (claims, IP, correlation id).</summary>
public sealed class CurrentUser : ICurrentUser
{
    public const string CorrelationIdItem = "CorrelationId";

    private readonly IHttpContextAccessor _accessor;

    public CurrentUser(IHttpContextAccessor accessor) => _accessor = accessor;

    private ClaimsPrincipal? Principal => _accessor.HttpContext?.User;

    public Guid? UserId
    {
        get
        {
            var value = Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? Principal?.FindFirst("sub")?.Value;
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    public string? Email => Principal?.FindFirst(ClaimTypes.Email)?.Value;

    public string? IpAddress => _accessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public string? CorrelationId =>
        _accessor.HttpContext?.Items.TryGetValue(CorrelationIdItem, out var value) == true
            ? value?.ToString()
            : null;

    public bool IsAuthenticated => Principal?.Identity?.IsAuthenticated ?? false;
}
