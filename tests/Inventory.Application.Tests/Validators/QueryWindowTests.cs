using Inventory.Application.Common.Queries;
using Inventory.Application.Features.Inventory;

namespace Inventory.Application.Tests.Validators;

public sealed class QueryWindowTests
{
    private static readonly DateTime NOW = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData("none")]
    [InlineData("from")]
    [InlineData("to")]
    [InlineData("both")]
    public void Optional_dates_always_resolve_to_a_bounded_inclusive_window(string mode)
    {
        var from = NOW.AddDays(-100);
        var request = new GetMovementsQuery(FromUtc: mode is "from" or "both" ? from : null,
            ToUtc: mode is "to" or "both" ? NOW : null);
        var range = QueryDateRange.Resolve(request, NOW);
        Assert.True(range.ToUtc - range.FromUtc <= QueryDateRange.MaxSpan);
        Assert.Equal(mode is "from" or "both" ? from : NOW.AddDays(-366), range.FromUtc);
        Assert.Equal(mode == "from" ? from.AddDays(366) : NOW, range.ToUtc);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Date_extremes_do_not_overflow_when_resolving_one_sided_ranges(bool upper)
    {
        var request = upper ? new GetMovementsQuery(FromUtc: DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc))
            : new GetMovementsQuery(ToUtc: DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc));
        var range = QueryDateRange.Resolve(request, NOW);
        Assert.Equal(range.FromUtc, range.ToUtc);
    }

    [Theory]
    [InlineData(366, true)]
    [InlineData(367, false)]
    [InlineData(-1, false)]
    public void Date_validation_accepts_366_days_and_rejects_wider_or_reversed_ranges(int days, bool valid)
    {
        Assert.Equal(valid, new GetMovementsQueryValidator().Validate(new GetMovementsQuery(FromUtc: NOW, ToUtc: NOW.AddDays(days))).IsValid);
    }
}
