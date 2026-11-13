using FluentValidation;
using Inventory.Application.Common.Abstractions;
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

public sealed class CreateAdjustmentCommandValidator : AbstractValidator<CreateAdjustmentCommand>
{
    public CreateAdjustmentCommandValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
        RuleFor(x => x.Justification).NotEmpty().WithMessage("Adjustments require a justification.");
        RuleFor(x => x.IdempotencyKey).NotEmpty();
        RuleFor(x => x.Lines).NotEmpty()
            .Must(lines => lines is null || lines.All(l => l is not null) && lines.Select(l => l.ProductId).Distinct().Count() == lines.Count)
            .WithMessage("A product may appear only once per movement.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductId).NotEmpty();
            line.RuleFor(l => l.UnitCost).PrecisionScale(18, 4, ignoreTrailingZeros: true).GreaterThanOrEqualTo(0);
            line.RuleFor(l => l.Quantity).PrecisionScale(18, 4, ignoreTrailingZeros: true).GreaterThan(0)
                .WithMessage("Adjustment count must be positive; use an exit to reach zero stock.");
        });
    }
}

public sealed class CreateAdjustmentCommandHandler : IRequestHandler<CreateAdjustmentCommand, MovementDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IMovementNumberGenerator _numbers;
    private readonly IPermissionChecker _permissions;

    public CreateAdjustmentCommandHandler(
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

    public async Task<MovementDto> Handle(CreateAdjustmentCommand request, CancellationToken cancellationToken)
    {
        await _permissions.EnsureWarehouseAccessAsync(request.WarehouseId, cancellationToken);
        var replay = await InventorySupport.FindReplayAsync(
            _db, _currentUser.UserId ?? Guid.Empty, request, cancellationToken);
        if (replay is not null) return replay;
        await InventorySupport.EnsureWarehouseExistsAsync(_db, request.WarehouseId, cancellationToken);
        var minimums = await InventorySupport.LoadProductMinimumsAsync(
            _db, request.Lines.Select(l => l.ProductId), cancellationToken);

        // Each line records the new counted (absolute) quantity for the product.
        var movementLines = request.Lines
            .Select(l => new InventoryMovementLine(l.ProductId, l.Quantity, l.UnitCost))
            .ToList();

        var number = await _numbers.NextAsync(MovementType.Adjustment, cancellationToken);
        var movement = InventoryMovement.CreateAdjustment(
            number, request.WarehouseId, _currentUser.UserId ?? Guid.Empty, _clock.UtcNow,
            movementLines, request.Justification);

        foreach (var line in request.Lines)
        {
            var stock = await InventorySupport.GetOrCreateStockAsync(
                _db, line.ProductId, request.WarehouseId, cancellationToken);
            stock.AdjustTo(line.Quantity);
            stock.CheckMinimum(minimums[line.ProductId]);
        }

        movement.SetIdempotency(request.IdempotencyKey, InventorySupport.RequestHash(request));
        _db.InventoryMovements.Add(movement);
        await _db.SaveChangesAsync(cancellationToken);

        return movement.ToDto();
    }
}
