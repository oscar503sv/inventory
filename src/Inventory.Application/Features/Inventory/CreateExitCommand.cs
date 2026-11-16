using FluentValidation;
using Inventory.Domain.Inventory;
using MediatR;
using Shared.Contracts.Inventory;

namespace Inventory.Application.Features.Inventory;

/// <summary>Registers an inventory exit (stock decrease) — ADR-001 movement type Exit. Negative stock is rejected.</summary>
public sealed record CreateExitCommand(
    Guid WarehouseId,
    IReadOnlyList<MovementLineInput> Lines,
    string? Notes = null,
    Guid IdempotencyKey = default) : IRequest<MovementDto>, IInventoryCommand;

public sealed class CreateExitCommandValidator : MovementCommandValidator<CreateExitCommand>
{
    public CreateExitCommandValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
    }
}

public sealed class CreateExitCommandHandler(InventoryMovementWriter writer) : IRequestHandler<CreateExitCommand, MovementDto>
{
    public Task<MovementDto> Handle(CreateExitCommand request, CancellationToken cancellationToken)
        => writer.WriteAsync(request, MovementType.Exit, request.WarehouseId, null, request.Notes, cancellationToken);
}
