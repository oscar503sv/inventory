using FluentValidation;
using Inventory.Application.Common.Abstractions;
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

public sealed class CreateTransferCommandValidator : AbstractValidator<CreateTransferCommand>
{
    public CreateTransferCommandValidator()
    {
        RuleFor(x => x.SourceWarehouseId).NotEmpty();
        RuleFor(x => x.DestinationWarehouseId).NotEmpty()
            .NotEqual(x => x.SourceWarehouseId)
            .WithMessage("Source and destination warehouses must differ.");
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty()
            .Must(lines => lines is null || lines.All(l => l is not null) && lines.Select(l => l.ProductId).Distinct().Count() == lines.Count)
            .WithMessage("A product may appear only once per movement.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.UnitCost).PrecisionScale(18, 4, ignoreTrailingZeros: true).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.Quantity).PrecisionScale(18, 4, ignoreTrailingZeros: true).GreaterThan(0);
        });
    }
}

public sealed class CreateTransferCommandHandler : IRequestHandler<CreateTransferCommand, MovementDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IMovementNumberGenerator _numbers;
    private readonly IPermissionChecker _permissions;

    public CreateTransferCommandHandler(
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

    public async Task<MovementDto> Handle(CreateTransferCommand request, CancellationToken cancellationToken)
    {
        await _permissions.EnsureWarehouseAccessAsync(request.SourceWarehouseId, cancellationToken);
        await _permissions.EnsureWarehouseAccessAsync(request.DestinationWarehouseId, cancellationToken);
        var replay = await InventorySupport.FindReplayAsync(
            _db, _currentUser.UserId ?? Guid.Empty, request, cancellationToken);
        if (replay is not null) return replay;
        await InventorySupport.EnsureWarehouseExistsAsync(_db, request.SourceWarehouseId, cancellationToken);
        await InventorySupport.EnsureWarehouseExistsAsync(_db, request.DestinationWarehouseId, cancellationToken);
        var minimums = await InventorySupport.LoadProductMinimumsAsync(
            _db, request.Lines.Select(l => l.ProductId), cancellationToken);

        var lines = request.Lines
            .Select(l => new InventoryMovementLine(l.ProductId, l.Quantity, l.UnitCost))
            .ToList();

        var number = await _numbers.NextAsync(MovementType.Transfer, cancellationToken);
        var movement = InventoryMovement.CreateTransfer(
            number, request.SourceWarehouseId, request.DestinationWarehouseId,
            _currentUser.UserId ?? Guid.Empty, _clock.UtcNow, lines, request.Notes);

        foreach (var line in request.Lines)
        {
            var source = await InventorySupport.GetStockOrThrowAsync(
                _db, line.ProductId, request.SourceWarehouseId, cancellationToken);
            source.Decrease(line.Quantity);
            source.CheckMinimum(minimums[line.ProductId]);

            var destination = await InventorySupport.GetOrCreateStockAsync(
                _db, line.ProductId, request.DestinationWarehouseId, cancellationToken);
            destination.Increase(line.Quantity);
        }

        movement.SetIdempotency(request.IdempotencyKey, InventorySupport.RequestHash(request));
        _db.InventoryMovements.Add(movement);
        await _db.SaveChangesAsync(cancellationToken);

        return movement.ToDto();
    }
}
