using FluentAssertions;
using Inventory.Domain.Common;
using Inventory.Domain.Inventory;

namespace Inventory.Domain.Tests.Inventory;

public class StockTests
{
    private static Stock NewStock() => new(Guid.NewGuid(), Guid.NewGuid());

    [Fact]
    public void AvailableQuantity_is_quantity_minus_reserved()
    {
        var stock = NewStock();
        stock.Increase(100);
        stock.Reserve(30);

        stock.AvailableQuantity.Should().Be(70);
    }

    [Fact]
    public void Increase_adds_to_quantity()
    {
        var stock = NewStock();
        stock.Increase(50);
        stock.Increase(25);

        stock.Quantity.Should().Be(75);
    }

    [Fact]
    public void Decrease_below_zero_is_rejected()
    {
        var stock = NewStock();
        stock.Increase(10);

        var act = () => stock.Decrease(15);

        act.Should().Throw<DomainException>().WithMessage("*Negative stock*");
    }

    [Fact]
    public void Decrease_below_reserved_is_rejected()
    {
        var stock = NewStock();
        stock.Increase(100);
        stock.Reserve(80);

        var act = () => stock.Decrease(30); // would leave 70 < 80 reserved

        act.Should().Throw<DomainException>().WithMessage("*reserved*");
    }

    [Fact]
    public void Reserve_more_than_available_is_rejected()
    {
        var stock = NewStock();
        stock.Increase(40);

        var act = () => stock.Reserve(50);

        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void AdjustTo_negative_is_rejected()
    {
        var stock = NewStock();

        var act = () => stock.AdjustTo(-1);

        act.Should().Throw<DomainException>().WithMessage("*Negative stock*");
    }

    [Fact]
    public void AdjustTo_sets_absolute_quantity()
    {
        var stock = NewStock();
        stock.Increase(10);

        stock.AdjustTo(42);

        stock.Quantity.Should().Be(42);
    }
}
