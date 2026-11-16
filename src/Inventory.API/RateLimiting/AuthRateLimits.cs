using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Threading.RateLimiting;
using Inventory.API.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.API.RateLimiting;

public sealed class AuthRateLimits : IDisposable
{
    public const string PolicyName = "authentication";
    private readonly PartitionedRateLimiter<string> _accounts;

    public AuthRateLimits(AuthRateLimitSettings settings)
    {
        _accounts = PartitionedRateLimiter.Create<string, string>(key =>
            RateLimitPartition.GetFixedWindowLimiter(key, _ => Window(settings.AccountPermitLimit, settings.WindowSeconds)));
    }

    public RateLimitLease AcquireAccount(string email)
    {
        var normalized = email.Trim().ToLowerInvariant();
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
        return _accounts.AttemptAcquire(key);
    }

    public static FixedWindowRateLimiterOptions Window(int permits, int seconds) => new()
    {
        PermitLimit = permits,
        Window = TimeSpan.FromSeconds(seconds),
        QueueLimit = 0,
        AutoReplenishment = true
    };

    public static Task RejectAsync(HttpContext context, RateLimitLease lease)
    {
        if (lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds))
                .ToString(CultureInfo.InvariantCulture);
        return ProblemResponses.WriteAsync(context, new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Too many requests",
            Detail = "Too many requests. Please try again later."
        });
    }

    public void Dispose() => _accounts.Dispose();
}
