namespace Inventory.Domain.Common;

/// <summary>Base class for entities, identified by a <see cref="Guid"/> surrogate key.</summary>
public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    public override bool Equals(object? obj)
        => obj is Entity other && other.GetType() == GetType() && other.Id == Id && Id != Guid.Empty;

    public override int GetHashCode() => Id.GetHashCode();
}
