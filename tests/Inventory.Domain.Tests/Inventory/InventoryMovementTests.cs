using FluentAssertions;
using Inventory.Domain.Common;
using Inventory.Domain.Inventory;
using Inventory.Domain.Inventory.Events;

namespace Inventory.Domain.Tests.Inventory;

public class InventoryMovementTests
{
    private static InventoryMovementLine Line() => new(Guid.NewGuid(), 10, 5);
    private static InventoryMovementLine[] Lines() => new[] { Line() };

    private readonly Guid _warehouse = Guid.NewGuid();
    private readonly Guid _user = Guid.NewGuid();
    private readonly DateTime _now = DateTime.UtcNow;

    [Fact]
    public void CreateEntry_raises_InventoryEntryCreated()
    {
        var movement = InventoryMovement.CreateEntry("MV-1", _warehouse, _user, _now, Lines());

        movement.MovementType.Should().Be(MovementType.Entry);
        movement.Status.Should().Be(MovementStatus.Confirmed);
        movement.DomainEvents.Should().ContainSingle(e => e is InventoryEntryCreatedDomainEvent);
    }

    [Fact]
    public void CreateExit_raises_InventoryExitCreated()
    {
        var movement = InventoryMovement.CreateExit("MV-2", _warehouse, _user, _now, Lines());

        movement.MovementType.Should().Be(MovementType.Exit);
        movement.DomainEvents.Should().ContainSingle(e => e is InventoryExitCreatedDomainEvent);
    }

    [Fact]
    public void Movement_without_user_is_rejected()
    {
        var act = () => InventoryMovement.CreateEntry("MV-3", _warehouse, Guid.Empty, _now, Lines());

        act.Should().Throw<DomainException>().WithMessage("*requires a user*");
    }

    [Fact]
    public void Movement_without_lines_is_rejected()
    {
        var act = () => InventoryMovement.CreateEntry(
            "MV-4", _warehouse, _user, _now, Array.Empty<InventoryMovementLine>());

        act.Should().Throw<DomainException>().WithMessage("*at least one line*");
    }

    [Fact]
    public void Adjustment_without_justification_is_rejected()
    {
        var act = () => InventoryMovement.CreateAdjustment("MV-5", _warehouse, _user, _now, Lines(), "  ");

        act.Should().Throw<DomainException>().WithMessage("*justification*");
    }

    [Fact]
    public void Adjustment_with_justification_is_created()
    {
        var movement = InventoryMovement.CreateAdjustment(
            "MV-6", _warehouse, _user, _now, Lines(), "Cycle count correction");

        movement.Notes.Should().Be("Cycle count correction");
        movement.DomainEvents.Should().ContainSingle(e => e is InventoryAdjustedDomainEvent);
    }

    [Fact]
    public void Transfer_to_same_warehouse_is_rejected()
    {
        var act = () => InventoryMovement.CreateTransfer("MV-7", _warehouse, _warehouse, _user, _now, Lines());

        act.Should().Throw<DomainException>().WithMessage("*must differ*");
    }

    [Fact]
    public void Transfer_sets_source_and_destination()
    {
        var destination = Guid.NewGuid();

        var movement = InventoryMovement.CreateTransfer("MV-8", _warehouse, destination, _user, _now, Lines());

        movement.WarehouseId.Should().Be(_warehouse);
        movement.DestinationWarehouseId.Should().Be(destination);
        movement.DomainEvents.Should().ContainSingle(e => e is InventoryTransferCreatedDomainEvent);
    }

    [Fact]
    public void Line_with_non_positive_quantity_is_rejected()
    {
        var act = () => new InventoryMovementLine(Guid.NewGuid(), 0, 5);

        act.Should().Throw<DomainException>().WithMessage("*quantity must be positive*");
    }
}
