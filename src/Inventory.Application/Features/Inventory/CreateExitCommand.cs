using FluentValidation;
using Inventory.Application.Common.Abstractions;
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

public sealed class CreateExitCommandValidator : AbstractValidator<CreateExitCommand>
{
    public CreateExitCommandValidator()
    {
        RuleFor(x => x.WarehouseId).NotEmpty();
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

public sealed class CreateExitCommandHandler : IRequestHandler<CreateExitCommand, MovementDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IMovementNumberGenerator _numbers;
    private readonly IPermissionChecker _permissions;

    public CreateExitCommandHandler(
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

    public async Task<MovementDto> Handle(CreateExitCommand request, CancellationToken cancellationToken)
    {
        await _permissions.EnsureWarehouseAccessAsync(request.WarehouseId, cancellationToken);
        var replay = await InventorySupport.FindReplayAsync(
            _db, _currentUser.UserId ?? Guid.Empty, request, cancellationToken);
        if (replay is not null) return replay;
        await InventorySupport.EnsureWarehouseExistsAsync(_db, request.WarehouseId, cancellationToken);
        var minimums = await InventorySupport.LoadProductMinimumsAsync(
            _db, request.Lines.Select(l => l.ProductId), cancellationToken);

        var lines = request.Lines
            .Select(l => new InventoryMovementLine(l.ProductId, l.Quantity, l.UnitCost))
            .ToList();

        var number = await _numbers.NextAsync(MovementType.Exit, cancellationToken);
        var movement = InventoryMovement.CreateExit(
            number, request.WarehouseId, _currentUser.UserId ?? Guid.Empty, _clock.UtcNow, lines, request.Notes);

        foreach (var line in request.Lines)
        {
            var stock = await InventorySupport.GetStockOrThrowAsync(
                _db, line.ProductId, request.WarehouseId, cancellationToken);
            stock.Decrease(line.Quantity);
            stock.CheckMinimum(minimums[line.ProductId]);
        }

        movement.SetIdempotency(request.IdempotencyKey, InventorySupport.RequestHash(request));
        _db.InventoryMovements.Add(movement);
        await _db.SaveChangesAsync(cancellationToken);

        return movement.ToDto();
    }
}
