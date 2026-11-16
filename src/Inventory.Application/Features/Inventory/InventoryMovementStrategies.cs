using Inventory.Application.Common.Abstractions;
using Inventory.Domain.Inventory;

namespace Inventory.Application.Features.Inventory;

public interface IInventoryMovementStrategy
{
    MovementType Type { get; }
    Task ApplyAsync(IApplicationDbContext db, Guid warehouseId, Guid? destinationId,
        IReadOnlyList<MovementLineInput> lines, IReadOnlyDictionary<Guid, decimal> minimums, CancellationToken ct);
}

public sealed class EntryStockStrategy : IInventoryMovementStrategy
{
    public MovementType Type => MovementType.Entry;
    public async Task ApplyAsync(IApplicationDbContext db, Guid warehouseId, Guid? destinationId,
        IReadOnlyList<MovementLineInput> lines, IReadOnlyDictionary<Guid, decimal> minimums, CancellationToken ct)
    {
        foreach (var line in lines)
        {
            var stock = await InventorySupport.GetOrCreateStockAsync(db, line.ProductId, warehouseId, ct);
            stock.Increase(line.Quantity);
        }
    }
}

public sealed class ExitStockStrategy : IInventoryMovementStrategy
{
    public MovementType Type => MovementType.Exit;
    public async Task ApplyAsync(IApplicationDbContext db, Guid warehouseId, Guid? destinationId,
        IReadOnlyList<MovementLineInput> lines, IReadOnlyDictionary<Guid, decimal> minimums, CancellationToken ct)
    {
        foreach (var line in lines)
        {
            var stock = await InventorySupport.GetStockOrThrowAsync(db, line.ProductId, warehouseId, ct);
            stock.Decrease(line.Quantity);
            stock.CheckMinimum(minimums[line.ProductId]);
        }
    }
}

public sealed class TransferStockStrategy : IInventoryMovementStrategy
{
    public MovementType Type => MovementType.Transfer;
    public async Task ApplyAsync(IApplicationDbContext db, Guid warehouseId, Guid? destinationId,
        IReadOnlyList<MovementLineInput> lines, IReadOnlyDictionary<Guid, decimal> minimums, CancellationToken ct)
    {
        foreach (var line in lines)
        {
            var source = await InventorySupport.GetStockOrThrowAsync(db, line.ProductId, warehouseId, ct);
            source.Decrease(line.Quantity);
            source.CheckMinimum(minimums[line.ProductId]);
            var destination = await InventorySupport.GetOrCreateStockAsync(db, line.ProductId, destinationId!.Value, ct);
            destination.Increase(line.Quantity);
        }
    }
}

public sealed class AdjustmentStockStrategy : IInventoryMovementStrategy
{
    public MovementType Type => MovementType.Adjustment;
    public async Task ApplyAsync(IApplicationDbContext db, Guid warehouseId, Guid? destinationId,
        IReadOnlyList<MovementLineInput> lines, IReadOnlyDictionary<Guid, decimal> minimums, CancellationToken ct)
    {
        foreach (var line in lines)
        {
            var stock = await InventorySupport.GetOrCreateStockAsync(db, line.ProductId, warehouseId, ct);
            stock.AdjustTo(line.Quantity);
            stock.CheckMinimum(minimums[line.ProductId]);
        }
    }
}
