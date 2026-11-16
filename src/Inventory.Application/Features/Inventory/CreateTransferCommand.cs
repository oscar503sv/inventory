using FluentValidation;
using Inventory.Domain.Inventory;
using MediatR;
using Shared.Contracts.Inventory;

namespace Inventory.Application.Features.Inventory;

/// <summary>
/// Transfers stock between two warehouses — ADR-001 movement type Transfer. The whole operation is
/// atomic (a single SaveChanges/transaction): source decreases and destination increases together.
/// </summary>
public sealed record CreateTransferCommand(
    Guid SourceWarehouseId,
    Guid DestinationWarehouseId,
    IReadOnlyList<MovementLineInput> Lines,
    string? Notes = null,
    Guid IdempotencyKey = default) : IRequest<MovementDto>, IInventoryCommand;

public sealed class CreateTransferCommandValidator : MovementCommandValidator<CreateTransferCommand>
{
    public CreateTransferCommandValidator() : base(command => command.Notes)
    {
        RuleFor(x => x.SourceWarehouseId).NotEmpty();
        RuleFor(x => x.DestinationWarehouseId).NotEmpty().NotEqual(x => x.SourceWarehouseId)
            .WithMessage("Source and destination warehouses must differ.");
    }
}

public sealed class CreateTransferCommandHandler(InventoryMovementWriter writer) : IRequestHandler<CreateTransferCommand, MovementDto>
{
    public Task<MovementDto> Handle(CreateTransferCommand request, CancellationToken cancellationToken)
        => writer.WriteAsync(request, MovementType.Transfer, request.SourceWarehouseId, request.DestinationWarehouseId, request.Notes, cancellationToken);
}
