using Inventory.API.Authentication;
using Inventory.Application.Common.Exceptions;
using Inventory.Domain.Common;
using Microsoft.AspNetCore.Mvc;

namespace Inventory.API.Middleware;

/// <summary>Maps domain/application exceptions to RFC 7807 ProblemDetails responses.</summary>
public sealed class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var (status, title) = exception switch
        {
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed"),
            NotFoundException => (StatusCodes.Status404NotFound, "Resource not found"),
            ForbiddenAccessException => (StatusCodes.Status403Forbidden, "Forbidden"),
            ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
            DomainException => (StatusCodes.Status422UnprocessableEntity, "Business rule violation"),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred")
        };

        if (status == StatusCodes.Status500InternalServerError)
            _logger.LogError(exception, "Unhandled exception");
        else
            _logger.LogWarning("{Title}: {Message}", title, exception.Message);

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = status == StatusCodes.Status500InternalServerError ? "An unexpected error occurred." : exception.Message
        };

        if (context.Items.TryGetValue(CurrentUser.CorrelationIdItem, out var correlationId))
            problem.Extensions["correlationId"] = correlationId;

        if (exception is ValidationException validation)
            problem.Extensions["errors"] = validation.Errors;

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    }
}
