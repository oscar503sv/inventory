using System.Text.Json;

namespace Shared.Contracts.Audit;

public sealed record AuditLogDto(
    Guid Id, Guid? UserId, DateTime TimestampUtc, string Entity, string Action,
    JsonElement? OldValues, JsonElement? NewValues, string? IpAddress, string? CorrelationId);
