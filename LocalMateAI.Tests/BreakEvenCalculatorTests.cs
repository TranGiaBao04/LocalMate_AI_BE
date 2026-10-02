using LocalMateAI.Application.DTOs.Dashboard;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.Dashboard;

namespace LocalMateAI.Tests;

public sealed class BreakEvenCalculatorTests
{
    private static readonly DateOnly October = new(2026, 10, 1);
    private static readonly DateTime GeneratedAt = new(2026, 10, 1, 5, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void FirstDayOfCurrentMonth_ProjectsFromOneDay()
    {
        var result = BreakEvenCalculator.Calculate(October, new DateOnly(2026, 10, 1), 5_000_000m,
            new PaidRevenueTotals(5, 177000m), GeneratedAt);

        Assert.Equal(new AdminDashboardBreakEvenResponse("2026-10", 5_000_000m, 177000m, 5, 4_823_000m, 3.5m,
            1, 31, 5_487_000m, false, "VND", GeneratedAt), result);
    }

    [Fact]
    public void MidMonth_UsesDaysElapsedSoFar()
    {
        var result = BreakEvenCalculator.Calculate(October, new DateOnly(2026, 10, 10), 5_000_000m,
            new PaidRevenueTotals(10, 1_000_000m), GeneratedAt);

        Assert.Equal(10, result.DaysElapsed);
        Assert.Equal(3_100_000m, result.Projected);
        Assert.Equal(20.0m, result.ProgressPercent);
    }

    [Fact]
    public void PastMonth_ProjectionEqualsRevenue()
    {
        var result = BreakEvenCalculator.Calculate(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 1), 5_000_000m,
            new PaidRevenueTotals(3, 97000m), GeneratedAt);

        Assert.Equal("2026-09", result.Month);
        Assert.Equal(30, result.DaysElapsed);
        Assert.Equal(30, result.DaysInMonth);
        Assert.Equal(97000m, result.Projected);
    }

    [Fact]
    public void RevenueAboveTarget_IsAchieved_WithZeroRemaining()
    {
        var result = BreakEvenCalculator.Calculate(October, new DateOnly(2026, 10, 20), 5_000_000m,
            new PaidRevenueTotals(100, 5_085_000m), GeneratedAt);

        Assert.True(result.IsAchieved);
        Assert.Equal(0m, result.Remaining);
        Assert.Equal(101.7m, result.ProgressPercent);
    }

    [Fact]
    public void RevenueExactlyTarget_IsAchieved()
    {
        var result = BreakEvenCalculator.Calculate(October, new DateOnly(2026, 10, 20), 5_000_000m,
            new PaidRevenueTotals(1, 5_000_000m), GeneratedAt);

        Assert.True(result.IsAchieved);
        Assert.Equal(100.0m, result.ProgressPercent);
    }

    [Fact]
    public void NoRevenue_ReturnsZeros()
    {
        var result = BreakEvenCalculator.Calculate(October, new DateOnly(2026, 10, 5), 5_000_000m,
            new PaidRevenueTotals(0, 0m), GeneratedAt);

        Assert.Equal(0m, result.ProgressPercent);
        Assert.Equal(0m, result.Projected);
        Assert.Equal(5_000_000m, result.Remaining);
        Assert.False(result.IsAchieved);
    }

    [Fact]
    public void ProgressPercent_RoundsHalfAwayFromZero()
    {
        // 2.250.000 / 5.000.000 = 45,0%; 2.252.500 / 5.000.000 = 45,05% ⇒ 45,1%.
        var result = BreakEvenCalculator.Calculate(October, new DateOnly(2026, 10, 5), 5_000_000m,
            new PaidRevenueTotals(1, 2_252_500m), GeneratedAt);

        Assert.Equal(45.1m, result.ProgressPercent);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("2026-10")]
    [InlineData("2026-09")]
    [InlineData(" 2000-01 ")]
    public void MonthValidator_AcceptsValidMonths(string? month)
    {
        Assert.True(MonthValidator().Validate(new DashboardBreakEvenQuery { Month = month }).IsValid);
    }

    [Theory]
    [InlineData("2026-1")]
    [InlineData("10-2026")]
    [InlineData("2026-10-01")]
    [InlineData("abc")]
    [InlineData("2026-11")]
    [InlineData("1999-12")]
    [InlineData("0001-01")]
    public void MonthValidator_RejectsInvalidMonths(string month)
    {
        var error = Assert.Single(MonthValidator().Validate(new DashboardBreakEvenQuery { Month = month }).Errors);

        Assert.Equal("Month", error.PropertyName);
    }

    private static DashboardBreakEvenQueryValidator MonthValidator() =>
        new(new FixedTimeProvider(new DateTimeOffset(2026, 10, 1, 5, 0, 0, TimeSpan.Zero)));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
