using Inventory.Domain.Common;

namespace Inventory.Domain.Catalog;

public sealed class UnitOfMeasure : Entity
{
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;

    private UnitOfMeasure() { }

    public UnitOfMeasure(string code, string name)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new DomainException("Unit of measure code is required.");
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Unit of measure name is required.");

        Code = code.Trim().ToUpperInvariant();
        Name = name.Trim();
    }
}
