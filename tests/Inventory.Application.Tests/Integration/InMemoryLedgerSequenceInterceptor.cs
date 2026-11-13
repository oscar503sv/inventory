using Inventory.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Inventory.Application.Tests.Integration;

// EF InMemory does not generate non-key identity values. Reproduce PostgreSQL's sequence
// so these application tests exercise the same persisted ordering as the real provider.
internal sealed class InMemoryLedgerSequenceInterceptor : SaveChangesInterceptor
{
    private long _sequence;

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } db)
            foreach (var entry in db.ChangeTracker.Entries<InventoryMovement>().Where(e => e.State == EntityState.Added))
                entry.Property(m => m.LedgerSequence).CurrentValue = ++_sequence;
        return ValueTask.FromResult(result);
    }
}
