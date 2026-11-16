using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Inventory.Domain.Inventory;

namespace Inventory.Application.Features.Inventory;

public sealed class InventoryMovementWriter
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUser _currentUser;
    private readonly IDateTimeProvider _clock;
    private readonly IMovementNumberGenerator _numbers;
    private readonly IPermissionChecker _permissions;
    private readonly IReadOnlyDictionary<MovementType, IInventoryMovementStrategy> _strategies;

    public InventoryMovementWriter(IApplicationDbContext db, ICurrentUser currentUser, IDateTimeProvider clock,
        IMovementNumberGenerator numbers, IPermissionChecker permissions, IEnumerable<IInventoryMovementStrategy> strategies)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
        _numbers = numbers;
        _permissions = permissions;
        _strategies = strategies.ToDictionary(strategy => strategy.Type);
    }

    public async Task<MovementWriteResult> WriteAsync(IInventoryCommand request, MovementType type,
        Guid warehouseId, Guid? destinationId, string? notes, CancellationToken ct)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is not { } userId || userId == Guid.Empty)
            throw new UnauthorizedException("Authentication is required.");

        await _permissions.EnsureWarehouseAccessAsync(warehouseId, ct);
        if (destinationId is { } destination) await _permissions.EnsureWarehouseAccessAsync(destination, ct);
        var replay = await InventorySupport.FindReplayAsync(_db, userId, request, ct);
        if (replay is not null) return new(replay, IsReplay: true);

        await InventorySupport.EnsureWarehouseActiveAsync(_db, warehouseId, ct);
        if (destinationId is { } target) await InventorySupport.EnsureWarehouseActiveAsync(_db, target, ct);
        var minimums = await InventorySupport.LoadProductMinimumsAsync(_db, request.Lines.Select(line => line.ProductId), ct);
        var lines = request.Lines.Select(line => new InventoryMovementLine(line.ProductId, line.Quantity, line.UnitCost)).ToList();
        var stocks = await MovementStockSet.LoadAsync(_db, request.Lines.Select(line => line.ProductId), warehouseId, destinationId, ct);
        var number = await _numbers.NextAsync(type, ct);
        var now = _clock.UtcNow;
        var movement = type switch
        {
            MovementType.Entry => InventoryMovement.CreateEntry(number, warehouseId, userId, now, lines, notes),
            MovementType.Exit => InventoryMovement.CreateExit(number, warehouseId, userId, now, lines, notes),
            MovementType.Transfer => InventoryMovement.CreateTransfer(number, warehouseId,
                destinationId ?? throw new ArgumentException("A transfer requires a destination."), userId, now, lines, notes),
            MovementType.Adjustment => InventoryMovement.CreateAdjustment(number, warehouseId, userId, now, lines, notes!),
            _ => throw new ArgumentOutOfRangeException(nameof(type))
        };
        _strategies[type].Apply(stocks, warehouseId, destinationId, request.Lines, minimums, ct);
        movement.SetIdempotency(request.IdempotencyKey, InventorySupport.RequestHash(request));
        _db.InventoryMovements.Add(movement);
        await _db.SaveChangesAsync(ct);
        return new(movement.ToDto(), IsReplay: false);
    }
}
