using Inventory.Application.Features.Inventory;

namespace Inventory.Application.Tests.Validators;

public sealed class MovementLinesValidatorTests
{
    [Theory]
    [InlineData("empty")]
    [InlineData("null")]
    [InlineData("null-item")]
    [InlineData("duplicate")]
    [InlineData("empty-product")]
    [InlineData("zero-quantity")]
    [InlineData("negative-quantity")]
    [InlineData("fractional-quantity")]
    [InlineData("overflow-quantity")]
    [InlineData("negative-cost")]
    [InlineData("fractional-cost")]
    public void All_movement_types_enforce_the_same_line_rules(string scenario)
    {
        var id = Guid.NewGuid();
        IReadOnlyList<MovementLineInput> lines = scenario switch
        {
            "empty" => [],
            "null" => null!,
            "null-item" => [null!],
            "duplicate" => [new(id, 1), new(id, 2)],
            "empty-product" => [new(Guid.Empty, 1)],
            "zero-quantity" => [new(id, 0)],
            "negative-quantity" => [new(id, -1)],
            "fractional-quantity" => [new(id, 1.00001m)],
            "overflow-quantity" => [new(id, 100000000000000m)],
            "negative-cost" => [new(id, 1, -1)],
            "fractional-cost" => [new(id, 1, 0.00001m)],
            _ => throw new ArgumentOutOfRangeException(nameof(scenario))
        };
        var warehouse = Guid.NewGuid();
        var key = Guid.NewGuid();
        Assert.False(new CreateEntryCommandValidator().Validate(new CreateEntryCommand(warehouse, lines, IdempotencyKey: key)).IsValid);
        Assert.False(new CreateExitCommandValidator().Validate(new CreateExitCommand(warehouse, lines, IdempotencyKey: key)).IsValid);
        Assert.False(new CreateTransferCommandValidator().Validate(new CreateTransferCommand(warehouse, Guid.NewGuid(), lines, IdempotencyKey: key)).IsValid);
        Assert.False(new CreateAdjustmentCommandValidator().Validate(new CreateAdjustmentCommand(warehouse, lines, "Physical count", key)).IsValid);
    }

    [Fact]
    public void Positive_lines_with_four_decimals_and_zero_cost_are_valid_for_every_type()
    {
        var warehouse = Guid.NewGuid();
        var key = Guid.NewGuid();
        MovementLineInput[] lines = [new(Guid.NewGuid(), 1.00010m, 0)];
        Assert.True(new CreateEntryCommandValidator().Validate(new CreateEntryCommand(warehouse, lines, IdempotencyKey: key)).IsValid);
        Assert.True(new CreateExitCommandValidator().Validate(new CreateExitCommand(warehouse, lines, IdempotencyKey: key)).IsValid);
        Assert.True(new CreateTransferCommandValidator().Validate(new CreateTransferCommand(warehouse, Guid.NewGuid(), lines, IdempotencyKey: key)).IsValid);
        Assert.True(new CreateAdjustmentCommandValidator().Validate(new CreateAdjustmentCommand(warehouse, lines, "Physical count", key)).IsValid);
    }
}
