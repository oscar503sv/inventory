using FluentValidation;
using Inventory.Domain.Inventory;
using MediatR;

namespace Inventory.Application.Features.Inventory;

/// <summary>Registers an inventory entry (stock increase) — ADR-001 movement type Entry.</summary>
public sealed record CreateEntryCommand(
    Guid WarehouseId,
    IReadOnlyList<MovementLineInput> Lines,
    string? Notes = null,
    Guid IdempotencyKey = default) : IRequest<MovementWriteResult>, IInventoryCommand;

public sealed class CreateEntryCommandValidator : MovementCommandValidator<CreateEntryCommand>
{
    public CreateEntryCommandValidator() : base(command => command.Notes)
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
    }
}

public sealed class CreateEntryCommandHandler(InventoryMovementWriter writer) : IRequestHandler<CreateEntryCommand, MovementWriteResult>
{
    public Task<MovementWriteResult> Handle(CreateEntryCommand request, CancellationToken cancellationToken)
        => writer.WriteAsync(request, MovementType.Entry, request.WarehouseId, null, request.Notes, cancellationToken);
}
