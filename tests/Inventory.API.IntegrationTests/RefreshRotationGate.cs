using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Inventory.API.IntegrationTests;

// Both requests must reach the conditional UPDATE before either can consume the token.
public sealed class RefreshRotationGate : DbCommandInterceptor
{
    private int _remaining;
    private TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public void Arm()
    {
        _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        _remaining = 2;
    }

    public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Volatile.Read(ref _remaining) == 0
            || !command.CommandText.StartsWith("UPDATE refresh_tokens", StringComparison.Ordinal))
            return result;

        var ready = _ready;
        if (Interlocked.Decrement(ref _remaining) == 0) ready.TrySetResult();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        return result;
    }
}
