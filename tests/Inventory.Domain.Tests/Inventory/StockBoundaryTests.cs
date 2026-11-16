using Inventory.Domain.Common;
using Inventory.Domain.Inventory;

namespace Inventory.Domain.Tests.Inventory;

public sealed class StockBoundaryTests
{
    [Fact]
    public void Accumulation_beyond_numeric_storage_is_rejected_without_mutation()
    {
        var stock = new Stock(Guid.NewGuid(), Guid.NewGuid());
        stock.Increase(InventoryNumericLimits.MaxStockQuantity);
        var version = stock.Version;
        Assert.Throws<DomainException>(() => stock.Increase(0.0001m));
        Assert.Equal(InventoryNumericLimits.MaxStockQuantity, stock.Quantity);
        Assert.Equal(version, stock.Version);
        Assert.Throws<DomainException>(() => stock.AdjustTo(decimal.MaxValue));
        Assert.Equal(version, stock.Version);
    }

    [Fact]
    public void Very_large_amounts_fail_as_domain_errors_instead_of_decimal_overflow()
    {
        var stock = new Stock(Guid.NewGuid(), Guid.NewGuid());
        stock.Increase(10);
        stock.Reserve(1);
        Assert.Throws<DomainException>(() => stock.Increase(decimal.MaxValue));
        Assert.Throws<DomainException>(() => stock.Reserve(decimal.MaxValue));
        Assert.Equal(10, stock.Quantity);
        Assert.Equal(1, stock.ReservedQuantity);
    }
}
