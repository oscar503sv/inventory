using Inventory.API.Authentication;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;

namespace Inventory.API.Middleware;

public static class ProblemResponses
{
    public static Task WriteAsync(HttpContext context, ProblemDetails problem)
    {
        problem.Extensions["correlationId"] = context.Items[CurrentUser.CorrelationIdItem];
        context.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;
        return context.Response.WriteAsJsonAsync(problem, options: (System.Text.Json.JsonSerializerOptions?)null,
            contentType: "application/problem+json", cancellationToken: context.RequestAborted);
    }

    public static Task WriteStatusAsync(StatusCodeContext status)
        => WriteAsync(status.HttpContext, new ProblemDetails
        {
            Status = status.HttpContext.Response.StatusCode,
            Title = ReasonPhrases.GetReasonPhrase(status.HttpContext.Response.StatusCode)
        });
}
