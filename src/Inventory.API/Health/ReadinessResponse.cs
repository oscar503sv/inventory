using Inventory.API.Middleware;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Inventory.API.Health;

public static class ReadinessResponse
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        if (report.Status == HealthStatus.Healthy)
            return context.Response.WriteAsJsonAsync(new { status = "Healthy" }, context.RequestAborted);

        // Health reports may contain connection errors. Publish only a generic diagnosis.
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status503ServiceUnavailable,
            Title = "Service unavailable",
            Detail = "The database is not ready."
        };
        return ProblemResponses.WriteAsync(context, problem);
    }
}
