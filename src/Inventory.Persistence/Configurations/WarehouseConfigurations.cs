using Inventory.Domain.Warehouses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventory.Persistence.Configurations;

public sealed class WarehouseConfiguration : IEntityTypeConfiguration<Warehouse>
{
    public void Configure(EntityTypeBuilder<Warehouse> builder)
    {
        builder.ToTable("warehouses");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Code).HasMaxLength(32).IsRequired();
        builder.Property(w => w.Name).HasMaxLength(256).IsRequired();
        builder.Property(w => w.Address).HasMaxLength(512);
        builder.Property(w => w.Status).HasConversion<string>().HasMaxLength(16);
        builder.HasIndex(w => w.Code).IsUnique();

        builder.Ignore(w => w.DomainEvents);
    }
}
