using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Shared.Contracts.Common;

namespace Inventory.Application.Common.Queries;

public interface IPagedQuery
{
    int Page { get; }
    int PageSize { get; }
}

public interface IDateRangeQuery
{
    DateTime? FromUtc { get; }
    DateTime? ToUtc { get; }
}

public abstract class PagedQueryValidator<T> : AbstractValidator<T> where T : IPagedQuery
{
    protected PagedQueryValidator()
    {
        RuleFor(q => q.Page).InclusiveBetween(1, QueryPaging.MaxPage);
        RuleFor(q => q.PageSize).InclusiveBetween(1, QueryPaging.MaxPageSize);
    }
}

public static class QueryValidation
{
    public static void AddDateRangeRules<T>(this AbstractValidator<T> validator) where T : IDateRangeQuery
    {
        validator.RuleFor(q => q.FromUtc).Must(IsUtc).WithMessage("Use an ISO 8601 UTC timestamp with Z.");
        validator.RuleFor(q => q.ToUtc).Must(IsUtc).WithMessage("Use an ISO 8601 UTC timestamp with Z.");
        validator.RuleFor(q => q).Must(q => q.FromUtc is null || q.ToUtc is null || q.FromUtc <= q.ToUtc)
            .WithMessage("FromUtc must not be later than ToUtc.");
        validator.RuleFor(q => q).Must(q => q.FromUtc is null || q.ToUtc is null
                || q.ToUtc.Value - q.FromUtc.Value <= QueryDateRange.MaxSpan)
            .WithMessage("Date ranges cannot exceed 366 days.");
    }

    private static bool IsUtc(DateTime? value) => value is null || value.Value.Kind == DateTimeKind.Utc;
}

/// <summary>Inclusive bounded query window. Ledger readers still use older rows for opening balances.</summary>
public sealed record QueryDateRange(DateTime FromUtc, DateTime ToUtc)
{
    public static readonly TimeSpan MaxSpan = TimeSpan.FromDays(366);

    public static QueryDateRange Resolve(IDateRangeQuery request, DateTime nowUtc)
    {
        var to = request.ToUtc ?? (request.FromUtc is { } start
            ? new DateTime(Math.Min(DateTime.MaxValue.Ticks, start.Ticks + MaxSpan.Ticks), DateTimeKind.Utc)
            : nowUtc);
        var from = request.FromUtc ?? new DateTime(Math.Max(DateTime.MinValue.Ticks, to.Ticks - MaxSpan.Ticks), DateTimeKind.Utc);
        return new(from, to);
    }
}

public static class QueryPaging
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 200;
    public const int MaxPage = 1000;

    public static int Offset(int page, int pageSize) => checked((page - 1) * pageSize);

    public static async Task<PagedResult<T>> ToPageAsync<T>(
        this IQueryable<T> query, int page, int pageSize, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip(Offset(page, pageSize)).Take(pageSize).ToListAsync(ct);
        return new PagedResult<T>(items, page, pageSize, total);
    }
}
