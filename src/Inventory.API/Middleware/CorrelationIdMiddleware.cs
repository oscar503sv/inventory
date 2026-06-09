using Inventory.API.Authentication;
using Serilog.Context;

namespace Inventory.API.Middleware;

/// <summary>
/// Ensures every request carries a CorrelationId (PRD: Observability — "Every request must include a
/// CorrelationId"). Reads an incoming <c>X-Correlation-Id</c> header or generates one, exposes it on
/// the response, in <see cref="HttpContext.Items"/>, and in the Serilog logging scope.
/// </summary>
public sealed class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-Id";

    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var incoming)
                            && !string.IsNullOrWhiteSpace(incoming)
            ? incoming.ToString()
            : Guid.NewGuid().ToString();

        context.Items[CurrentUser.CorrelationIdItem] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
