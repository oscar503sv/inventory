using FluentAssertions;
using Inventory.Domain.Catalog;
using Inventory.Domain.Catalog.Events;
using Inventory.Domain.Common;

namespace Inventory.Domain.Tests.Catalog;

public class ProductTests
{
    private static Product Create(decimal minimumStock = 0) => Product.Create(
        "SKU-1", "C-1", "Widget", "desc",
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), minimumStock);

    [Fact]
    public void Create_raises_ProductCreated()
    {
        var product = Create();

        product.IsActive.Should().BeTrue();
        product.DomainEvents.Should().ContainSingle(e => e is ProductCreatedDomainEvent);
    }

    [Fact]
    public void Create_without_sku_is_rejected()
    {
        var act = () => Product.Create(
            "  ", "C", "Name", "d", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0);

        act.Should().Throw<DomainException>().WithMessage("*SKU*");
    }

    [Fact]
    public void Create_with_negative_minimum_stock_is_rejected()
    {
        var act = () => Create(-5);

        act.Should().Throw<DomainException>().WithMessage("*Minimum stock*");
    }

    [Fact]
    public void Update_raises_ProductUpdated()
    {
        var product = Create();
        product.ClearDomainEvents();

        product.Update("New", "d2", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 3);

        product.Name.Should().Be("New");
        product.MinimumStock.Should().Be(3);
        product.DomainEvents.Should().ContainSingle(e => e is ProductUpdatedDomainEvent);
    }

    [Fact]
    public void Code_defaults_to_sku_when_blank()
    {
        var product = Product.Create(
            "SKU-9", "", "Name", "d", Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 0);

        product.Code.Should().Be("SKU-9");
    }
}
