using FluentValidation;
using Inventory.Application.Common.Abstractions;
using Inventory.Domain.Inventory;
using MediatR;
using Shared.Contracts.Inventory;

namespace Inventory.Application.Features.Inventory;

/// <summary>Registers an inventory entry (stock increase) — ADR-001 movement type Entry.</summary>
public sealed record CreateEntryCommand(
    Guid WarehouseId,
    IReadOnlyList<MovementLineInput> Lines,
    string? Notes = null) : IRequest<MovementDto>;

public sealed class CreateEntryCommandValidator : AbstractValidator<CreateEntryCommand>
{
    public CreateEntryCommandValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty();
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.Quantity).GreaterThan(0);
            line.RuleFor(l => l.UnitCost).GreaterThanOrEqualTo(0);
        });
    }
}

public sealed class CreateEntryCommandHandler : IRequestHandler<CreateEntryCommand, MovementDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IMovementNumberGenerator _numbers;
    private readonly IPermissionChecker _permissions;

    public CreateEntryCommandHandler(
        IApplicationDbContext db,
        ICurrentUser currentUser,
        IDateTimeProvider clock,
        IMovementNumberGenerator numbers,
        IPermissionChecker permissions)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _numbers = numbers;
        _permissions = permissions;
    }

    public async Task<MovementDto> Handle(CreateEntryCommand request, CancellationToken cancellationToken)
    {
        await _permissions.EnsureWarehouseAccessAsync(request.WarehouseId, cancellationToken);
        await InventorySupport.EnsureWarehouseExistsAsync(_db, request.WarehouseId, cancellationToken);
        await InventorySupport.LoadProductMinimumsAsync(
            _db, request.Lines.Select(l => l.ProductId), cancellationToken);

        var lines = request.Lines
            .Select(l => new InventoryMovementLine(l.ProductId, l.Quantity, l.UnitCost))
            .ToList();

        var number = await _numbers.NextAsync(MovementType.Entry, cancellationToken);
        var movement = InventoryMovement.CreateEntry(
            number, request.WarehouseId, _currentUser.UserId ?? Guid.Empty, _clock.UtcNow, lines, request.Notes);

        foreach (var line in request.Lines)
        {
            var stock = await InventorySupport.GetOrCreateStockAsync(
                _db, line.ProductId, request.WarehouseId, cancellationToken);
            stock.Increase(line.Quantity);
        }

        _db.InventoryMovements.Add(movement);
        await _db.SaveChangesAsync(cancellationToken);

        return movement.ToDto();
    }
}
