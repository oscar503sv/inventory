namespace Inventory.Application.Common.Abstractions;

/// <summary>Ambient information about the caller, resolved per request from the HTTP context.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
    string? Email { get; }
    string? IpAddress { get; }
    string? CorrelationId { get; }
    bool IsAuthenticated { get; }
}
