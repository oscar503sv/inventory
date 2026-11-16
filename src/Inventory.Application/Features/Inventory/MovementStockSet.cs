using Inventory.Application.Common.Abstractions;
using Inventory.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace Inventory.Application.Features.Inventory;

/// <summary>Tracked stocks loaded once for the complete movement, including both transfer endpoints.</summary>
public sealed class MovementStockSet
{
    private readonly IApplicationDbContext _db;
    private readonly Dictionary<(Guid Product, Guid Warehouse), Stock> _stocks;
    private MovementStockSet(IApplicationDbContext db, IEnumerable<Stock> stocks)
    {
        _db = db;
        _stocks = stocks.ToDictionary(stock => (stock.ProductId, stock.WarehouseId));
    }

    public static async Task<MovementStockSet> LoadAsync(IApplicationDbContext db, IEnumerable<Guid> products,
        Guid warehouse, Guid? destination, CancellationToken ct)
    {
        var productIds = products.Distinct().ToArray();
        var warehouses = destination is { } other ? new[] { warehouse, other } : [warehouse];
        var stocks = await db.Stocks.TagWith("Inventory.MovementStocks")
            .Where(stock => productIds.Contains(stock.ProductId) && warehouses.Contains(stock.WarehouseId)).ToListAsync(ct);
        return new MovementStockSet(db, stocks);
    }

    public Stock GetOrCreate(Guid product, Guid warehouse)
    {
        if (_stocks.TryGetValue((product, warehouse), out var stock)) return stock;
        stock = new Stock(product, warehouse);
        _db.Stocks.Add(stock);
        _stocks.Add((product, warehouse), stock);
        return stock;
    }

}
