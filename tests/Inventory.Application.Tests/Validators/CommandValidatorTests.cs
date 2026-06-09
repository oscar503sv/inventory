using FluentAssertions;
using Inventory.Application.Features.Auth;
using Inventory.Application.Features.Inventory;

namespace Inventory.Application.Tests.Validators;

public class CommandValidatorTests
{
    [Fact]
    public void Login_requires_email_and_password()
    {
        var result = new LoginCommandValidator().Validate(new LoginCommand("", ""));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(LoginCommand.Email));
        result.Errors.Should().Contain(e => e.PropertyName == nameof(LoginCommand.Password));
    }

    [Fact]
    public void Adjustment_requires_justification()
    {
        var command = new CreateAdjustmentCommand(
            Guid.NewGuid(),
            [new MovementLineInput(Guid.NewGuid(), 10)],
            Justification: "");

        var result = new CreateAdjustmentCommandValidator().Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateAdjustmentCommand.Justification));
    }

    [Fact]
    public void Transfer_rejects_same_source_and_destination()
    {
        var warehouse = Guid.NewGuid();
        var command = new CreateTransferCommand(
            warehouse, warehouse,
            [new MovementLineInput(Guid.NewGuid(), 5)]);

        var result = new CreateTransferCommandValidator().Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == nameof(CreateTransferCommand.DestinationWarehouseId));
    }

    [Fact]
    public void Exit_rejects_non_positive_line_quantity()
    {
        var command = new CreateExitCommand(
            Guid.NewGuid(),
            [new MovementLineInput(Guid.NewGuid(), 0)]);

        var result = new CreateExitCommandValidator().Validate(command);

        result.IsValid.Should().BeFalse();
    }
}
