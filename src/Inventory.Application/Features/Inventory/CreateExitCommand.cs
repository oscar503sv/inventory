using FluentValidation;
using Inventory.Domain.Inventory;
using MediatR;

namespace Inventory.Application.Features.Inventory;

/// <summary>Registers an inventory exit (stock decrease) — ADR-001 movement type Exit. Negative stock is rejected.</summary>
public sealed record CreateExitCommand(
    Guid WarehouseId,
    IReadOnlyList<MovementLineInput> Lines,
    string? Notes = null,
    Guid IdempotencyKey = default) : IRequest<MovementWriteResult>, IInventoryCommand;

public sealed class CreateExitCommandValidator : MovementCommandValidator<CreateExitCommand>
{
    public CreateExitCommandValidator() : base(command => command.Notes)
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
    }
}

public sealed class CreateExitCommandHandler(InventoryMovementWriter writer) : IRequestHandler<CreateExitCommand, MovementWriteResult>
{
    public Task<MovementWriteResult> Handle(CreateExitCommand request, CancellationToken cancellationToken)
        => writer.WriteAsync(request, MovementType.Exit, request.WarehouseId, null, request.Notes, cancellationToken);
}
