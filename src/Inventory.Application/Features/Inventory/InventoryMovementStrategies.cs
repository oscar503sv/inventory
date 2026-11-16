using Inventory.Domain.Inventory;

namespace Inventory.Application.Features.Inventory;

public interface IInventoryMovementStrategy
{
    MovementType Type { get; }
    void Apply(MovementStockSet stocks, Guid warehouseId, Guid? destinationId,
        IReadOnlyList<MovementLineInput> lines, IReadOnlyDictionary<Guid, decimal> minimums, CancellationToken ct);
}

public sealed class EntryStockStrategy : IInventoryMovementStrategy
{
    public MovementType Type => MovementType.Entry;
    public void Apply(MovementStockSet stocks, Guid warehouseId, Guid? destinationId,
        IReadOnlyList<MovementLineInput> lines, IReadOnlyDictionary<Guid, decimal> minimums, CancellationToken ct)
    {
        foreach (var line in lines)
        {
            var stock = stocks.GetOrCreate(line.ProductId, warehouseId);
            stock.Increase(line.Quantity);
        }
    }
}

public sealed class ExitStockStrategy : IInventoryMovementStrategy
{
    public MovementType Type => MovementType.Exit;
    public void Apply(MovementStockSet stocks, Guid warehouseId, Guid? destinationId,
        IReadOnlyList<MovementLineInput> lines, IReadOnlyDictionary<Guid, decimal> minimums, CancellationToken ct)
    {
        foreach (var line in lines)
        {
            var stock = stocks.GetRequired(line.ProductId, warehouseId);
            stock.Decrease(line.Quantity);
            stock.CheckMinimum(minimums[line.ProductId]);
        }
    }
}

public sealed class TransferStockStrategy : IInventoryMovementStrategy
{
    public MovementType Type => MovementType.Transfer;
    public void Apply(MovementStockSet stocks, Guid warehouseId, Guid? destinationId,
        IReadOnlyList<MovementLineInput> lines, IReadOnlyDictionary<Guid, decimal> minimums, CancellationToken ct)
    {
        foreach (var line in lines)
        {
            var source = stocks.GetRequired(line.ProductId, warehouseId);
            source.Decrease(line.Quantity);
            source.CheckMinimum(minimums[line.ProductId]);
            var destination = stocks.GetOrCreate(line.ProductId, destinationId!.Value);
            destination.Increase(line.Quantity);
        }
    }
}

public sealed class AdjustmentStockStrategy : IInventoryMovementStrategy
{
    public MovementType Type => MovementType.Adjustment;
    public void Apply(MovementStockSet stocks, Guid warehouseId, Guid? destinationId,
        IReadOnlyList<MovementLineInput> lines, IReadOnlyDictionary<Guid, decimal> minimums, CancellationToken ct)
    {
        foreach (var line in lines)
        {
            var stock = stocks.GetOrCreate(line.ProductId, warehouseId);
            stock.AdjustTo(line.Quantity);
            stock.CheckMinimum(minimums[line.ProductId]);
        }
    }
}
