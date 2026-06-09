using Inventory.Domain.Common;

namespace Inventory.Domain.Audit;

/// <summary>
/// An immutable audit record for a critical action. Once created it is never modified or deleted.
/// </summary>
public sealed class AuditLog : Entity
{
    public Guid? UserId { get; private set; }
    public DateTime TimestampUtc { get; private set; }
    public string Entity { get; private set; } = null!;
    public string Action { get; private set; } = null!;
    public string? OldValues { get; private set; }
    public string? NewValues { get; private set; }
    public string? IpAddress { get; private set; }
    public string? CorrelationId { get; private set; }

    private AuditLog() { }

    public AuditLog(
        Guid? userId,
        DateTime timestampUtc,
        string entity,
        string action,
        string? oldValues,
        string? newValues,
        string? ipAddress,
        string? correlationId)
    {
        if (string.IsNullOrWhiteSpace(entity))
            throw new DomainException("Audit entity is required.");
        if (string.IsNullOrWhiteSpace(action))
            throw new DomainException("Audit action is required.");

        UserId = userId;
        TimestampUtc = timestampUtc;
        Entity = entity;
        Action = action;
        OldValues = oldValues;
        NewValues = newValues;
        IpAddress = ipAddress;
        CorrelationId = correlationId;
    }
}
