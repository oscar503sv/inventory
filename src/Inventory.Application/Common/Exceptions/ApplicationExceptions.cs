namespace Inventory.Application.Common.Exceptions;

/// <summary>A requested resource was not found.</summary>
public sealed class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message) { }

    public static NotFoundException For(string entity, object key)
        => new($"{entity} '{key}' was not found.");
}

/// <summary>The caller is authenticated but not allowed to perform the action (e.g. warehouse scope).</summary>
public sealed class ForbiddenAccessException : Exception
{
    public ForbiddenAccessException(string message = "You are not allowed to perform this action.")
        : base(message) { }
}

/// <summary>The request conflicts with the current state (e.g. duplicate unique value).</summary>
public sealed class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

/// <summary>Aggregated input validation failures (one entry per field).</summary>
public sealed class ValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
        => Errors = errors;
}

/// <summary>The supplied credentials no longer establish a valid session.</summary>
public sealed class UnauthorizedException(string message) : Exception(message);
