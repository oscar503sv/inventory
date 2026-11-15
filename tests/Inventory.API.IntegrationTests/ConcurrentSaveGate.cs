using Inventory.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Inventory.API.IntegrationTests;

// Forces two real HTTP requests to finish reading the same stock before either writes.
// The timeout fails the test instead of leaving a hanging server if one request never arrives.
public sealed class ConcurrentSaveGate : SaveChangesInterceptor
{
    private Guid _productId;
    private int _arrivals;
    private TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Arm(Guid productId)
    {
        _productId = productId;
        _arrivals = 0;
        _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (_productId == Guid.Empty || eventData.Context is null
            || !eventData.Context.ChangeTracker.Entries<InventoryMovement>()
                .Any(e => e.State == EntityState.Added && e.Entity.Lines.Any(l => l.ProductId == _productId)))
            return result;

        var ready = _ready;
        if (Interlocked.Increment(ref _arrivals) == 2)
        {
            _productId = Guid.Empty;
            ready.TrySetResult();
        }
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        return result;
    }
}
