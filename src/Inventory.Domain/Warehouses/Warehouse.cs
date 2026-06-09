using Inventory.Domain.Common;

namespace Inventory.Domain.Warehouses;

/// <summary>
/// A warehouse — the primary inventory unit. Stock is tracked per warehouse (ADR-002).
/// </summary>
public sealed class Warehouse : AggregateRoot
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string Address { get; private set; } = null!;
    public WarehouseStatus Status { get; private set; }

    private Warehouse() { }

    public static Warehouse Create(string code, string name, string address)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Warehouse code is required.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Warehouse name is required.");

        return new Warehouse
        {
            Code = code.Trim().ToUpperInvariant(),
            Name = name.Trim(),
            Address = address?.Trim() ?? string.Empty,
            Status = WarehouseStatus.Active
        };
    }

    public void Update(string name, string address)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Warehouse name is required.");

        Name = name.Trim();
        Address = address?.Trim() ?? string.Empty;
    }

    public void Activate() => Status = WarehouseStatus.Active;
    public void Deactivate() => Status = WarehouseStatus.Inactive;
}
