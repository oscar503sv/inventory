using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Inventory.Domain.Audit;
using Inventory.Domain.Catalog;
using Inventory.Domain.Common;
using Inventory.Domain.Identity;
using Inventory.Domain.Inventory;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inventory.Persistence;

public sealed class AppDbContext : DbContext, IApplicationDbContext
{
    // Every administrator profile/access PUT must use this same transaction-scoped lock.
    internal const long AdministratorUpdateLockKey = 471047;
    private readonly IDomainEventDispatcher _dispatcher;
    private List<IDomainEvent>? _pendingAdministratorEvents;

    public AppDbContext(DbContextOptions<AppDbContext> options, IDomainEventDispatcher dispatcher)
        : base(options)
    {
        _dispatcher = dispatcher;
    }

    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRole> UserRoles => Set<UserRole>();
    public DbSet<UserPermissionOverride> UserPermissionOverrides => Set<UserPermissionOverride>();
    public DbSet<WarehouseScope> WarehouseScopes => Set<WarehouseScope>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<UnitOfMeasure> UnitsOfMeasure => Set<UnitOfMeasure>();

    public DbSet<Warehouse> Warehouses => Set<Warehouse>();

    public DbSet<Stock> Stocks => Set<Stock>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<InventoryMovementLine> InventoryMovementLines => Set<InventoryMovementLine>();

    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var domainEvents = ChangeTracker
            .Entries<AggregateRoot>()
            .SelectMany(e => e.Entity.DomainEvents)
            .ToList();

        int result;
        try
        {
            result = await base.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            ChangeTracker.Clear();
            throw new ConflictException("Stock changed concurrently. Retry the complete request with the same idempotency key.");
        }
        catch (Exception ex) when (IsInventoryWriteConflict(ex))
        {
            ChangeTracker.Clear();
            throw new ConflictException("An inventory request conflicted with another write. Retry the complete request with the same idempotency key.");
        }

        // Dispatch after commit so handlers observe persisted state (ADR-004).
        if (domainEvents.Count > 0)
        {
            foreach (var entry in ChangeTracker.Entries<AggregateRoot>())
                entry.Entity.ClearDomainEvents();

            if (_pendingAdministratorEvents is not null)
                _pendingAdministratorEvents.AddRange(domainEvents);
            else
                await _dispatcher.DispatchAsync(domainEvents, cancellationToken);
        }

        return result;
    }

    public async Task<bool> TryRotateRefreshTokenAsync(
        Guid tokenId, RefreshToken replacement, DateTime nowUtc, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(cancellationToken);
        var affected = await RefreshTokens
            .Where(t => t.Id == tokenId && t.UserId == replacement.UserId
                && t.RevokedAtUtc == null && t.ExpiresAtUtc > nowUtc)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.RevokedAtUtc, nowUtc)
                .SetProperty(t => t.ReplacedByTokenHash, replacement.TokenHash), cancellationToken);
        if (affected != 1) return false;

        RefreshTokens.Add(replacement);
        await SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    public async Task ExecuteAdministratorUpdateAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        await using var transaction = await Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        _pendingAdministratorEvents = [];
        try
        {
            await Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({AdministratorUpdateLockKey})", cancellationToken);
            await operation();
            await transaction.CommitAsync(cancellationToken);
            var events = _pendingAdministratorEvents;
            _pendingAdministratorEvents = null;
            if (events.Count > 0) await _dispatcher.DispatchAsync(events, cancellationToken);
        }
        finally
        {
            _pendingAdministratorEvents = null;
        }
    }

    private static bool IsInventoryWriteConflict(Exception exception)
    {
        // Npgsql's execution strategy wraps transient PostgreSQL errors in InvalidOperationException.
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is PostgresException postgres)
                return postgres.SqlState is PostgresErrorCodes.DeadlockDetected or PostgresErrorCodes.SerializationFailure
                    || (postgres.SqlState == PostgresErrorCodes.UniqueViolation && postgres.ConstraintName is
                        "IX_stocks_ProductId_WarehouseId" or "IX_inventory_movements_CreatedBy_IdempotencyKey");
        }
        return false;
    }
}
