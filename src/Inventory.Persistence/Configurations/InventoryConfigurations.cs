using Inventory.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Persistence.Configurations;

public sealed class StockConfiguration : IEntityTypeConfiguration<Stock>
{
    public void Configure(EntityTypeBuilder<Stock> builder)
    {
        builder.ToTable("stocks");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Quantity).HasPrecision(18, 4);
        builder.Property(s => s.ReservedQuantity).HasPrecision(18, 4);
        builder.Property(s => s.Version).IsConcurrencyToken();
        builder.Ignore(s => s.AvailableQuantity);
        builder.Ignore(s => s.DomainEvents);
        builder.HasIndex(s => new { s.ProductId, s.WarehouseId }).IsUnique();
    }
}

public sealed class InventoryMovementConfiguration : IEntityTypeConfiguration<InventoryMovement>
{
    public void Configure(EntityTypeBuilder<InventoryMovement> builder)
    {
        builder.ToTable("inventory_movements");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.MovementNumber).HasMaxLength(32).IsRequired();
        builder.Property(m => m.MovementType).HasConversion<string>().HasMaxLength(16);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(m => m.Notes).HasMaxLength(1024);
        builder.Property(m => m.LedgerSequence).ValueGeneratedOnAdd();
        builder.Property(m => m.RequestHash).HasMaxLength(64);
        builder.HasIndex(m => m.LedgerSequence).IsUnique();
        builder.HasIndex(m => new { m.CreatedBy, m.IdempotencyKey }).IsUnique();
        builder.HasIndex(m => m.MovementNumber).IsUnique();
        builder.HasIndex(m => m.WarehouseId);
        builder.HasIndex(m => m.CreatedAtUtc);

        builder.HasMany(m => m.Lines).WithOne().HasForeignKey(l => l.MovementId).OnDelete(DeleteBehavior.Cascade);
        builder.Metadata.FindNavigation(nameof(InventoryMovement.Lines))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.Ignore(m => m.DomainEvents);
    }
}

public sealed class InventoryMovementLineConfiguration : IEntityTypeConfiguration<InventoryMovementLine>
{
    public void Configure(EntityTypeBuilder<InventoryMovementLine> builder)
    {
        builder.ToTable("inventory_movement_lines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Quantity).HasPrecision(18, 4);
        builder.Property(l => l.UnitCost).HasPrecision(18, 4);
        builder.HasIndex(l => l.ProductId);
    }
}
