using FluentValidation;
using Inventory.Domain.Inventory;
using MediatR;
using Shared.Contracts.Inventory;

namespace Inventory.Application.Features.Inventory;

/// <summary>
/// Adjusts stock to a counted absolute quantity (e.g. cycle count) — ADR-001 movement type Adjustment.
/// Each line's <c>Quantity</c> is the new physical quantity. A justification is mandatory.
/// </summary>
public sealed record CreateAdjustmentCommand(
    Guid WarehouseId,
    IReadOnlyList<MovementLineInput> Lines,
    string Justification,
    Guid IdempotencyKey = default) : IRequest<MovementDto>, IInventoryCommand;

public sealed class CreateAdjustmentCommandValidator : MovementCommandValidator<CreateAdjustmentCommand>
{
    public CreateAdjustmentCommandValidator() : base(command => command.Justification, "Adjustment count must be positive; use an exit to reach zero stock.")
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Justification).NotEmpty().WithMessage("Adjustments require a justification.");
    }
}

public sealed class CreateAdjustmentCommandHandler(InventoryMovementWriter writer) : IRequestHandler<CreateAdjustmentCommand, MovementDto>
{
    public Task<MovementDto> Handle(CreateAdjustmentCommand request, CancellationToken cancellationToken)
        => writer.WriteAsync(request, MovementType.Adjustment, request.WarehouseId, null, request.Justification, cancellationToken);
}
