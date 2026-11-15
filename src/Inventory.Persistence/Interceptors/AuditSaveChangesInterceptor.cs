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
    // Opt in business fields: new credential/hash/token properties can never enter the audit by default.
    private static readonly Dictionary<Type, HashSet<string>> AuditedProperties = new()
    {
        [typeof(Product)] = [nameof(Product.Sku), nameof(Product.Code), nameof(Product.Name),
            nameof(Product.Description), nameof(Product.CategoryId), nameof(Product.BrandId),
            nameof(Product.UnitOfMeasureId), nameof(Product.MinimumStock), nameof(Product.IsActive)],
        [typeof(Warehouse)] = [nameof(Warehouse.Code), nameof(Warehouse.Name), nameof(Warehouse.Address), nameof(Warehouse.Status)],
        [typeof(User)] = [nameof(User.Email), nameof(User.UserName), nameof(User.FullName),
            nameof(User.IsActive), nameof(User.HasAllWarehousesAccess)],
        [typeof(Role)] = [nameof(Role.Name), nameof(Role.Description)],
        [typeof(Permission)] = [nameof(Permission.Code), nameof(Permission.Description)],
        [typeof(UserRole)] = [nameof(UserRole.UserId), nameof(UserRole.RoleId)],
        [typeof(RolePermission)] = [nameof(RolePermission.RoleId), nameof(RolePermission.PermissionId)],
        [typeof(UserPermissionOverride)] = [nameof(UserPermissionOverride.UserId),
            nameof(UserPermissionOverride.PermissionId), nameof(UserPermissionOverride.OverrideType)],
        [typeof(WarehouseScope)] = [nameof(WarehouseScope.UserId), nameof(WarehouseScope.WarehouseId)],
        [typeof(InventoryMovement)] = [nameof(InventoryMovement.MovementNumber), nameof(InventoryMovement.MovementType),
            nameof(InventoryMovement.WarehouseId), nameof(InventoryMovement.DestinationWarehouseId),
            nameof(InventoryMovement.Status), nameof(InventoryMovement.CreatedBy),
            nameof(InventoryMovement.CreatedAtUtc), nameof(InventoryMovement.Notes)]
    };

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
            .Where(e => AuditedProperties.ContainsKey(e.Entity.GetType())
                        && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in audited)
            context.Add(CreateAuditLog(entry));
    }

    private AuditLog CreateAuditLog(EntityEntry entry)
    {
        var action = entry.State switch
        {
            EntityState.Added => "Created",
            EntityState.Modified => "Updated",
            EntityState.Deleted => "Deleted",
            _ => "Unknown"
        };
        return new AuditLog(
            userId: _currentUser.UserId,
            timestampUtc: _clock.UtcNow,
            entity: entry.Entity.GetType().Name,
            action: action,
            oldValues: entry.State is EntityState.Added ? null : Serialize(entry, original: true),
            newValues: entry.State is EntityState.Deleted ? null : Serialize(entry, original: false),
            ipAddress: _currentUser.IpAddress,
            correlationId: _currentUser.CorrelationId);
    }

    private static string Serialize(EntityEntry entry, bool original)
    {
        var values = new Dictionary<string, object?>();
        foreach (var prop in entry.Properties)
        {
            if (!prop.Metadata.IsPrimaryKey() && !AuditedProperties[entry.Entity.GetType()].Contains(prop.Metadata.Name)) continue;
            values[prop.Metadata.Name] = original ? prop.OriginalValue : prop.CurrentValue;
        }

        return JsonSerializer.Serialize(values);
    }
}
