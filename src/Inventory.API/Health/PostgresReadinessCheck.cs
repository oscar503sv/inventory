using Inventory.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Inventory.API.Health;

public sealed class PostgresReadinessCheck(AppDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        if (!await db.Database.CanConnectAsync(ct)) return HealthCheckResult.Unhealthy("PostgreSQL is unavailable.");
        if ((await db.Database.GetPendingMigrationsAsync(ct)).Any())
            return HealthCheckResult.Unhealthy("Database migrations are pending.");
        return HealthCheckResult.Healthy();
    }
}
