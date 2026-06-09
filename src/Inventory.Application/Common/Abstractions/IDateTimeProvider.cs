namespace Inventory.Application.Common.Abstractions;

/// <summary>Abstraction over the system clock so time-dependent logic stays testable.</summary>
public interface IDateTimeProvider
{
    DateTime UtcNow { get; }
}
