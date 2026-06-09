using Inventory.Domain.Common;

namespace Inventory.Domain.Catalog;

public sealed class Brand : Entity
{
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;

    private Brand() { }

    public Brand(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Brand name is required.");

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
    }

    public void Rename(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Brand name is required.");

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
    }
}
