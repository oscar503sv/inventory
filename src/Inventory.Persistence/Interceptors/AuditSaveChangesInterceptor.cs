using System.Text.Json;
using Inventory.Application.Common.Abstractions;
using Inventory.Domain.Audit;
using Inventory.Domain.Catalog;
using Inventory.Domain.Identity;
using Inventory.Domain.Inventory;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Inventory.Persistence.Interceptors;

/// <summary>
/// Writes an immutable <see cref="AuditLog"/> entry for every create/update/delete of a critical
/// entity. Audit records carry the acting user, timestamp, correlation id and IP (PRD: Audit Requirements).
/// </summary>
public sealed class AuditSaveChangesInterceptor : SaveChangesInterceptor
{
    private static readonly HashSet<Type> AuditedTypes =
    [
        typeof(Product), typeof(Warehouse), typeof(User), typeof(Role), typeof(InventoryMovement)
    ];

    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;

    public AuditSaveChangesInterceptor(ICurrentUser currentUser, IDateTimeProvider clock)
    {
        _currentUser = currentUser;
        _clock = clock;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
            WriteAuditEntries(eventData.Context);

        return base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData, InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
            WriteAuditEntries(eventData.Context);

        return base.SavingChanges(eventData, result);
    }

    private void WriteAuditEntries(DbContext context)
    {
        var audited = context.ChangeTracker.Entries()
            .Where(e => AuditedTypes.Contains(e.Entity.GetType())
                        && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in audited)
        {
            var action = entry.State switch
            {
                EntityState.Added => "Created",
                EntityState.Modified => "Updated",
                EntityState.Deleted => "Deleted",
                _ => "Unknown"
            };

            var log = new AuditLog(
                userId: _currentUser.UserId,
                timestampUtc: _clock.UtcNow,
                entity: entry.Entity.GetType().Name,
                action: action,
                oldValues: entry.State is EntityState.Added ? null : Serialize(entry, original: true),
                newValues: entry.State is EntityState.Deleted ? null : Serialize(entry, original: false),
                ipAddress: _currentUser.IpAddress,
                correlationId: _currentUser.CorrelationId);

            context.Add(log);
        }
    }

    private static string Serialize(EntityEntry entry, bool original)
    {
        var values = new Dictionary<string, object?>();
        foreach (var prop in entry.Properties)
        {
            if (prop.Metadata.IsPrimaryKey()) continue;
            values[prop.Metadata.Name] = original ? prop.OriginalValue : prop.CurrentValue;
        }

        return JsonSerializer.Serialize(values);
    }
}
