using FluentAssertions;
using Inventory.Application.Features.Inventory;
using Inventory.Domain.Catalog;
using Inventory.Domain.Common;
using Inventory.Domain.Warehouses;
using Inventory.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Tests.Integration;

/// <summary>
/// End-to-end exercise of the movement-based inventory flow (ADR-001) over an in-memory database:
/// entry/exit/transfer mutate stock, and the Kardex reconstructs the running balance.
/// </summary>
public class InventoryFlowTests
{
    private static AppDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase($"inv-{Guid.NewGuid()}").Options,
            new NoOpDomainEventDispatcher());

    private static async Task<(Guid productId, Guid warehouseId)> SeedAsync(AppDbContext db)
    {
        var category = new Category("Cat", "");
        var brand = new Brand("Brand", "");
        var uom = new UnitOfMeasure("EA", "Each");
        var warehouse = Warehouse.Create("WH1", "Main", "Addr");
        var product = Product.Create("SKU1", "C1", "Widget", "", category.Id, brand.Id, uom.Id, minimumStock: 5);

        db.Categories.Add(category);
        db.Brands.Add(brand);
        db.UnitsOfMeasure.Add(uom);
        db.Warehouses.Add(warehouse);
        db.Products.Add(product);
        await db.SaveChangesAsync();

        return (product.Id, warehouse.Id);
    }

    private (CreateEntryCommandHandler entry, CreateExitCommandHandler exit, CreateTransferCommandHandler transfer,
        GetKardexQueryHandler kardex, GetStockQueryHandler stock) Handlers(AppDbContext db)
    {
        var clock = new TestClock();
        var user = new TestCurrentUser();
        var numbers = new SequentialMovementNumberGenerator();
        var perms = new AllowAllPermissionChecker();

        return (
            new CreateEntryCommandHandler(db, user, clock, numbers, perms),
            new CreateExitCommandHandler(db, user, clock, numbers, perms),
            new CreateTransferCommandHandler(db, user, clock, numbers, perms),
            new GetKardexQueryHandler(db),
            new GetStockQueryHandler(db));
    }

    [Fact]
    public async Task Entry_then_exit_updates_stock_and_kardex()
    {
        var db = NewDb();
        var (productId, warehouseId) = await SeedAsync(db);
        var (entry, exit, _, kardex, stock) = Handlers(db);

        await entry.Handle(new CreateEntryCommand(
            warehouseId, [new MovementLineInput(productId, 100, 2)]), default);
        await exit.Handle(new CreateExitCommand(
            warehouseId, [new MovementLineInput(productId, 30)]), default);

        var stocks = await stock.Handle(new GetStockQuery(productId, warehouseId), default);
        stocks.Single().Quantity.Should().Be(70);
        stocks.Single().AvailableQuantity.Should().Be(70);

        var ledger = await kardex.Handle(new GetKardexQuery(productId, warehouseId), default);
        ledger.Should().HaveCount(2);
        ledger[0].RunningBalance.Should().Be(100);
        ledger[1].RunningBalance.Should().Be(70);
    }

    [Fact]
    public async Task Exit_beyond_available_stock_is_rejected()
    {
        var db = NewDb();
        var (productId, warehouseId) = await SeedAsync(db);
        var (entry, exit, _, _, _) = Handlers(db);

        await entry.Handle(new CreateEntryCommand(warehouseId, [new MovementLineInput(productId, 10)]), default);

        var act = async () => await exit.Handle(
            new CreateExitCommand(warehouseId, [new MovementLineInput(productId, 25)]), default);

        await act.Should().ThrowAsync<DomainException>().WithMessage("*Negative stock*");
    }

    [Fact]
    public async Task Transfer_moves_stock_between_warehouses_atomically()
    {
        var db = NewDb();
        var (productId, sourceId) = await SeedAsync(db);

        var destination = Warehouse.Create("WH2", "Secondary", "Addr2");
        db.Warehouses.Add(destination);
        await db.SaveChangesAsync();

        var (entry, _, transfer, _, stock) = Handlers(db);

        await entry.Handle(new CreateEntryCommand(sourceId, [new MovementLineInput(productId, 100)]), default);
        await transfer.Handle(new CreateTransferCommand(
            sourceId, destination.Id, [new MovementLineInput(productId, 40)]), default);

        var sourceStock = (await stock.Handle(new GetStockQuery(productId, sourceId), default)).Single();
        var destStock = (await stock.Handle(new GetStockQuery(productId, destination.Id), default)).Single();

        sourceStock.Quantity.Should().Be(60);
        destStock.Quantity.Should().Be(40);
    }
}
