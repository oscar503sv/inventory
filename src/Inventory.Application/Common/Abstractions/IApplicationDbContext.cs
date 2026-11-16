using Inventory.Domain.Audit;
using Inventory.Domain.Catalog;
using Inventory.Domain.Identity;
using Inventory.Domain.Inventory;
using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Common.Abstractions;

/// <summary>
/// The persistence port used by the application layer. PostgreSQL is the source of truth (ADR-005).
/// </summary>
public interface IApplicationDbContext
{
    // Identity
    DbSet<User> Users { get; }
    DbSet<Role> Roles { get; }
    DbSet<Permission> Permissions { get; }
    DbSet<RolePermission> RolePermissions { get; }
    DbSet<UserRole> UserRoles { get; }
    DbSet<UserPermissionOverride> UserPermissionOverrides { get; }
    DbSet<WarehouseScope> WarehouseScopes { get; }
    DbSet<RefreshToken> RefreshTokens { get; }

    // Catalog
    DbSet<Product> Products { get; }
    DbSet<Category> Categories { get; }
    DbSet<Brand> Brands { get; }
    DbSet<UnitOfMeasure> UnitsOfMeasure { get; }

    // Warehouses
    DbSet<Warehouse> Warehouses { get; }

    // Inventory
    DbSet<Stock> Stocks { get; }
    DbSet<InventoryMovement> InventoryMovements { get; }
    DbSet<InventoryMovementLine> InventoryMovementLines { get; }

    // Audit
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
    Task<bool> TryRotateRefreshTokenAsync(Guid tokenId, RefreshToken replacement, DateTime nowUtc, CancellationToken cancellationToken);
    Task ExecuteSessionUpdateAsync(Guid userId, Func<Task> operation, CancellationToken cancellationToken);
    Task ExecuteAdministratorUpdateAsync(Func<Task> operation, CancellationToken cancellationToken);
}
