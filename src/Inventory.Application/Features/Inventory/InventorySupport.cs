using System.Security.Cryptography;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Inventory.Application.Common.Abstractions;
using Inventory.Application.Common.Exceptions;
using Inventory.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Inventory;

namespace Inventory.Application.Features.Inventory;

/// <summary>Input line for an inventory movement command.</summary>
public sealed record MovementLineInput(Guid ProductId, decimal Quantity, decimal UnitCost = 0);

public interface IInventoryCommand
{
    Guid IdempotencyKey { get; }
}

internal static class InventorySupport
{
    private static readonly JsonSerializerOptions FingerprintJson = new()
    {
        Converters = { new CanonicalDecimalConverter() }
    };

    private sealed class CanonicalDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
            => reader.GetDecimal();

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options)
            => writer.WriteRawValue(value.ToString("G29", CultureInfo.InvariantCulture));
    }

    public static string RequestHash(IInventoryCommand request)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            request.GetType().Name + ":" + JsonSerializer.Serialize(request, request.GetType(), FingerprintJson))));

    public static async Task<MovementDto?> FindReplayAsync(
        IApplicationDbContext db, Guid userId, IInventoryCommand request, CancellationToken ct)
    {
        if (request.IdempotencyKey == Guid.Empty)
            throw new ValidationException(new Dictionary<string, string[]>
            {
                [nameof(request.IdempotencyKey)] = ["A non-empty idempotency key is required."]
            });
        var existing = await db.InventoryMovements.AsNoTracking().Include(m => m.Lines)
            .SingleOrDefaultAsync(m => m.CreatedBy == userId && m.IdempotencyKey == request.IdempotencyKey, ct);
        if (existing is null) return null;
        if (existing.RequestHash != RequestHash(request))
            throw new ConflictException("This idempotency key was already used with a different request.");
        return existing.ToDto();
    }

    public static MovementDto ToDto(this InventoryMovement m) => new(
        m.Id,
        m.MovementNumber,
        m.MovementType.ToString(),
        m.WarehouseId,
        m.DestinationWarehouseId,
        m.Status.ToString(),
        m.CreatedBy,
        m.CreatedAtUtc,
        m.Notes,
        m.Lines.OrderBy(l => l.ProductId).Select(l => new MovementLineDto(l.ProductId, l.Quantity, l.UnitCost)).ToList());

    public static async Task<Stock> GetOrCreateStockAsync(
        IApplicationDbContext db, Guid productId, Guid warehouseId, CancellationToken ct)
    {
        var stock = await db.Stocks
            .FirstOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId, ct);

        if (stock is null)
        {
            stock = new Stock(productId, warehouseId);
            db.Stocks.Add(stock);
        }

        return stock;
    }

    public static async Task<Stock> GetStockOrThrowAsync(
        IApplicationDbContext db, Guid productId, Guid warehouseId, CancellationToken ct)
        => await db.Stocks.FirstOrDefaultAsync(s => s.ProductId == productId && s.WarehouseId == warehouseId, ct)
           ?? throw new ConflictException($"No stock exists for product '{productId}' in warehouse '{warehouseId}'.");

    public static async Task EnsureWarehouseExistsAsync(
        IApplicationDbContext db, Guid warehouseId, CancellationToken ct)
    {
        if (!await db.Warehouses.AnyAsync(w => w.Id == warehouseId, ct))
            throw NotFoundException.For("Warehouse", warehouseId);
    }

    /// <summary>Loads the minimum-stock thresholds for the given products, validating they all exist.</summary>
    public static async Task<Dictionary<Guid, decimal>> LoadProductMinimumsAsync(
        IApplicationDbContext db, IEnumerable<Guid> productIds, CancellationToken ct)
    {
        var ids = productIds.Distinct().ToList();
        var products = await db.Products
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.MinimumStock })
            .ToListAsync(ct);

        if (products.Count != ids.Count)
        {
            var missing = ids.Except(products.Select(p => p.Id)).First();
            throw NotFoundException.For("Product", missing);
        }

        return products.ToDictionary(p => p.Id, p => p.MinimumStock);
    }
}
