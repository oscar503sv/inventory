namespace Shared.Contracts.Inventory;

public sealed record MovementLineDto(Guid ProductId, decimal Quantity, decimal UnitCost);

public sealed record MovementSummaryDto(
    Guid Id, string MovementNumber, string MovementType, Guid WarehouseId, Guid? DestinationWarehouseId,
    string Status, Guid CreatedBy, DateTime CreatedAtUtc, string Notes, int LineCount);

public sealed record MovementDto(
    Guid Id,
    string MovementNumber,
    string MovementType,
    Guid WarehouseId,
    Guid? DestinationWarehouseId,
    string Status,
    Guid CreatedBy,
    DateTime CreatedAtUtc,
    string Notes,
    IReadOnlyList<MovementLineDto> Lines);

public sealed record StockDto(
    Guid ProductId,
    Guid WarehouseId,
    decimal Quantity,
    decimal ReservedQuantity,
    decimal AvailableQuantity);

/// <summary>A single Kardex (stock ledger) row for a product in a warehouse.</summary>
public sealed record KardexEntryDto(
    DateTime TimestampUtc,
    string MovementNumber,
    string MovementType,
    Guid WarehouseId,
    decimal Quantity,
    decimal RunningBalance);
