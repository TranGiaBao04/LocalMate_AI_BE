using LocalMateAI.Application.DTOs.Dashboard;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.Dashboard;

namespace LocalMateAI.Tests;

public sealed class DashboardDateRangeQueryValidatorTests
{
    // 12:00 ngày 01/10/2026 giờ VN.
    private static readonly DateTimeOffset Noon = new(2026, 10, 1, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public void EmptyQuery_IsValid()
    {
        Assert.True(Validate(new DashboardDateRangeQuery()).IsValid);
    }

    [Theory]
    [InlineData("2026-13-01", null, "From")]
    [InlineData("2026-9-1", null, "From")]
    [InlineData(null, "abc", "To")]
    [InlineData(null, "01/10/2026", "To")]
    public void MalformedDate_FailsOnThatField(string? from, string? to, string field)
    {
        var result = Validate(new DashboardDateRangeQuery { From = from, To = to });

        var error = Assert.Single(result.Errors);
        Assert.Equal(field, error.PropertyName);
    }

    [Theory]
    [InlineData(null, "2026-10-02", "To")]
    [InlineData("2026-09-10", "2026-09-05", "From")]
    [InlineData("2026-10-05", null, "From")]
    [InlineData("2025-09-30", "2026-10-01", "From")]
    public void InvalidRange_FailsOnExpectedField(string? from, string? to, string field)
    {
        var result = Validate(new DashboardDateRangeQuery { From = from, To = to });

        var error = Assert.Single(result.Errors);
        Assert.Equal(field, error.PropertyName);
    }

    [Theory]
    [InlineData("2025-10-01", "2026-10-01")]
    [InlineData("2026-10-01", "2026-10-01")]
    [InlineData(" 2026-09-01 ", " 2026-09-30 ")]
    public void ValidRange_Passes(string from, string to)
    {
        Assert.True(Validate(new DashboardDateRangeQuery { From = from, To = to }).IsValid);
    }

    [Fact]
    public void Today_FollowsVietnamTime_NotUtc()
    {
        // 17:30 UTC ngày 30/09 = 00:30 VN ngày 01/10.
        var clock = new FixedTimeProvider(new DateTimeOffset(2026, 9, 30, 17, 30, 0, TimeSpan.Zero));
        var validator = new DashboardDateRangeQueryValidator(clock);

        Assert.True(validator.Validate(new DashboardDateRangeQuery { To = "2026-10-01" }).IsValid);
    }

    [Fact]
    public void Resolve_DefaultsToLast30DaysEndingToday()
    {
        var range = DashboardDateRules.Resolve(new DashboardDateRangeQuery(), new DateOnly(2026, 10, 1))!;

        Assert.Equal(new DateOnly(2026, 9, 2), range.From);
        Assert.Equal(new DateOnly(2026, 10, 1), range.To);
        Assert.Equal(30, range.DayCount);
    }

    [Fact]
    public void Range_ConvertsVietnamDaysToHalfOpenUtcWindow()
    {
        var range = new DashboardDateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1));

        Assert.Equal(new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc), range.StartUtc);
        Assert.Equal(new DateTime(2026, 10, 1, 17, 0, 0, DateTimeKind.Utc), range.EndUtc);
        Assert.Equal(DateTimeKind.Utc, range.StartUtc.Kind);
    }

    private static FluentValidation.Results.ValidationResult Validate(DashboardDateRangeQuery query) =>
        new DashboardDateRangeQueryValidator(new FixedTimeProvider(Noon)).Validate(query);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
