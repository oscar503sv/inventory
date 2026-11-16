using Inventory.Application.Features.Inventory;

namespace Inventory.Application.Tests.Validators;

public sealed class MovementBoundaryValidatorTests
{
    [Theory]
    [InlineData(500, 1024, true)]
    [InlineData(501, 10, false)]
    [InlineData(1, 1025, false)]
    public void Shared_limits_apply_to_all_movement_commands(int count, int textLength, bool valid)
    {
        var warehouse = Guid.NewGuid();
        var key = Guid.NewGuid();
        var lines = Enumerable.Range(0, count).Select(_ => new MovementLineInput(Guid.NewGuid(), 1, 0)).ToArray();
        var text = new string('n', textLength);
        Assert.Equal(valid, new CreateEntryCommandValidator().Validate(new CreateEntryCommand(warehouse, lines, text, key)).IsValid);
        Assert.Equal(valid, new CreateExitCommandValidator().Validate(new CreateExitCommand(warehouse, lines, text, key)).IsValid);
        Assert.Equal(valid, new CreateTransferCommandValidator().Validate(new CreateTransferCommand(warehouse, Guid.NewGuid(), lines, text, key)).IsValid);
        Assert.Equal(valid, new CreateAdjustmentCommandValidator().Validate(new CreateAdjustmentCommand(warehouse, lines, text, key)).IsValid);
    }
}
