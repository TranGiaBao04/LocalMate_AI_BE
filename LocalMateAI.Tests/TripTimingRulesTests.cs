using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.Trips;

namespace LocalMateAI.Tests;

public sealed class TripTimingRulesTests
{
    // 2026-09-26 15:00 giờ Việt Nam = 08:00 UTC.
    private static readonly DateTime Now = new(2026, 9, 26, 15, 0, 0, DateTimeKind.Unspecified);
    private static readonly DateOnly Today = new(2026, 9, 26);

    [Fact]
    public void ValidatePlannedDate_RejectsYesterday()
        => Assert.NotNull(TripTimingRules.ValidatePlannedDate(Today.AddDays(-1), Now));

    [Theory]
    [InlineData(0)]
    [InlineData(90)]
    public void ValidatePlannedDate_AcceptsTodayAndNinetyDaysAhead(int daysAhead)
        => Assert.Null(TripTimingRules.ValidatePlannedDate(Today.AddDays(daysAhead), Now));

    [Fact]
    public void ValidatePlannedDate_RejectsNinetyOneDaysAhead()
        => Assert.NotNull(TripTimingRules.ValidatePlannedDate(Today.AddDays(91), Now));

    [Fact]
    public void ValidatePlannedDate_AcceptsNull()
        => Assert.Null(TripTimingRules.ValidatePlannedDate(null, Now));

    [Fact]
    public void ValidateStartTime_AcceptsWithinFiveMinutesGraceToday()
        => Assert.Null(TripTimingRules.ValidateStartTime(Today, new TimeOnly(14, 56), Now));

    [Fact]
    public void ValidateStartTime_RejectsEarlierThanGraceToday()
        => Assert.NotNull(TripTimingRules.ValidateStartTime(Today, new TimeOnly(14, 54), Now));

    [Fact]
    public void ValidateStartTime_TreatsNullPlannedDateAsToday()
        => Assert.NotNull(TripTimingRules.ValidateStartTime(null, new TimeOnly(9, 0), Now));

    [Fact]
    public void ValidateStartTime_AcceptsMissingStartTimeEvenAfterDefault()
        => Assert.Null(TripTimingRules.ValidateStartTime(Today, null, Now));

    [Fact]
    public void ValidateStartTime_AcceptsEarlyTimeOnFutureDate()
        => Assert.Null(TripTimingRules.ValidateStartTime(Today.AddDays(1), new TimeOnly(6, 0), Now));

    [Fact]
    public void ValidateStartTime_DoesNotWrapAroundJustAfterMidnight()
    {
        var justAfterMidnight = new DateTime(2026, 9, 26, 0, 2, 0, DateTimeKind.Unspecified);

        Assert.Null(TripTimingRules.ValidateStartTime(Today, new TimeOnly(0, 0), justAfterMidnight));
    }

    [Theory]
    [InlineData(20, 0, 240, true)]   // đúng 24:00
    [InlineData(23, 0, 240, false)]  // qua nửa đêm
    [InlineData(0, 0, 1440, true)]
    public void ValidateWindow_RejectsOnlyWhenPastMidnight(int hour, int minute, int totalMinutes, bool valid)
    {
        var error = TripTimingRules.ValidateWindow(new TimeOnly(hour, minute), totalMinutes);

        Assert.Equal(valid, error is null);
    }

    [Fact]
    public void ValidateWindow_UsesDefaultStartTimeWhenMissing()
        => Assert.NotNull(TripTimingRules.ValidateWindow(null, 17 * 60)); // 08:00 + 17 giờ > 24:00

    [Fact]
    public void ResolveStart_DefaultsToTodayAtEight()
        => Assert.Equal(new DateTime(2026, 9, 26, 8, 0, 0), TripTimingRules.ResolveStart(null, null, Now));

    [Fact]
    public void ResolveStart_UsesGivenDateAndTime()
        => Assert.Equal(
            new DateTime(2026, 10, 3, 18, 30, 0),
            TripTimingRules.ResolveStart(new DateOnly(2026, 10, 3), new TimeOnly(18, 30), Now));

    [Fact]
    public void VietnamTime_IsSevenHoursAheadOfUtc()
        => Assert.Equal(Now, VietnamTime.Now(new FixedTimeProvider(new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero))));

    [Fact]
    public void Validator_ReportsTimingErrorsOnTheirOwnFields()
    {
        var validator = new TripRequestValidator(new FixedTimeProvider(new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero)));
        var request = new TripRequestDto(
            10.77, 106.69, 4, 0, 300000, [],
            PlannedDate: Today.AddDays(-1), StartTime: new TimeOnly(23, 0));

        var errors = validator.Validate(request).Errors.Select(error => error.PropertyName).ToList();

        Assert.Contains("PlannedDate", errors);
        Assert.Contains("DurationHours", errors); // 23:00 + 4 giờ vượt 24:00
    }

    [Fact]
    public void Validator_ReportsOnlyRangeErrorWhenDurationOutOfRange()
    {
        var validator = new TripRequestValidator(new FixedTimeProvider(new DateTimeOffset(2026, 9, 26, 8, 0, 0, TimeSpan.Zero)));
        var request = new TripRequestDto(10.77, 106.69, 30, 0, 300000, [], StartTime: new TimeOnly(23, 0));

        var errors = validator.Validate(request).Errors.Where(error => error.PropertyName == "DurationHours").ToList();

        Assert.Single(errors);
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
