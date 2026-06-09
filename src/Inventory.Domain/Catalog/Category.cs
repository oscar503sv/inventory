using Inventory.Domain.Common;

namespace Inventory.Domain.Catalog;

public sealed class Category : Entity
{
    public string Name { get; private set; } = null!;
    public string Description { get; private set; } = null!;

    private Category() { }

    public Category(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Category name is required.");

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
    }

    public void Rename(string name, string description)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Category name is required.");

        Name = name.Trim();
        Description = description?.Trim() ?? string.Empty;
    }
}
