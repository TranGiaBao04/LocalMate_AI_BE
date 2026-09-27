using LocalMateAI.Application.DTOs.Metro;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class MetroTimetableValidatorTests
{
    [Fact]
    public void Validate_AcceptsValidTimetable()
        => Assert.Empty(MetroTimetableValidator.Validate(MetroTimetableTestData.Valid()));

    [Fact]
    public void Validate_RejectsEffectiveToBeforeEffectiveFrom()
    {
        var timetable = MetroTimetableTestData.Valid() with { EffectiveTo = new DateOnly(2026, 8, 31) };

        AssertHasError(timetable, "effectiveTo");
    }

    [Fact]
    public void Validate_RejectsMissingStationOffset()
    {
        var timetable = MetroTimetableTestData.Valid() with
        {
            StationOffsets = MetroTimetableTestData.Offsets().Where(offset => offset.StationOrder != 7).ToList()
        };

        AssertHasError(timetable, "stationOffsets");
    }

    [Fact]
    public void Validate_RejectsNonZeroOffsetAtOriginStation()
    {
        var offsets = MetroTimetableTestData.Offsets();
        offsets[0] = offsets[0] with { MinutesTowardSuoiTien = 1 };

        AssertHasError(MetroTimetableTestData.Valid() with { StationOffsets = offsets }, "Station 1");
    }

    [Fact]
    public void Validate_RejectsOffsetsNotIncreasingAlongDirection()
    {
        var offsets = MetroTimetableTestData.Offsets();
        offsets[5] = offsets[5] with { MinutesTowardSuoiTien = offsets[4].MinutesTowardSuoiTien };

        AssertHasError(MetroTimetableTestData.Valid() with { StationOffsets = offsets }, "minutesTowardSuoiTien must increase");
    }

    [Fact]
    public void Validate_RejectsHeadwayNotStartingAtFirstDeparture()
    {
        var service = MetroTimetableTestData.Valid().Services[0] with { FirstDeparture = new TimeOnly(4, 30) };

        AssertHasError(WithFirstService(service), "headways[0].from must equal firstDeparture");
    }

    [Fact]
    public void Validate_RejectsGapBetweenHeadways()
    {
        var service = MetroTimetableTestData.Valid().Services[0] with
        {
            Headways =
            [
                new MetroHeadway(new TimeOnly(5, 0), new TimeOnly(7, 0), 10),
                new MetroHeadway(new TimeOnly(7, 30), new TimeOnly(22, 0), 15)
            ]
        };

        AssertHasError(WithFirstService(service), "previous headway");
    }

    [Fact]
    public void Validate_RejectsHeadwaysNotCoveringLastDeparture()
    {
        var service = MetroTimetableTestData.Valid().Services[0] with
        {
            Headways =
            [
                new MetroHeadway(new TimeOnly(5, 0), new TimeOnly(7, 0), 10),
                new MetroHeadway(new TimeOnly(7, 0), new TimeOnly(21, 0), 15)
            ]
        };

        AssertHasError(WithFirstService(service), "cover up to lastDeparture");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(61)]
    public void Validate_RejectsHeadwayMinutesOutOfRange(int minutes)
    {
        var original = MetroTimetableTestData.Valid().Services[0];
        var service = original with { Headways = [original.Headways[0] with { Minutes = minutes }, original.Headways[1]] };

        AssertHasError(WithFirstService(service), "minutes must be between");
    }

    [Fact]
    public void Validate_RejectsDirectionMissingADay()
    {
        var service = MetroTimetableTestData.Valid().Services[0] with
        {
            Days = [DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday]
        };

        AssertHasError(WithFirstService(service), "TowardSuoiTien has no service on Thursday");
    }

    [Fact]
    public void Validate_RejectsTwoServicesOnSameDayAndDirection()
    {
        var service = MetroTimetableTestData.Valid().Services[0] with
        {
            Days = [.. MetroTimetableTestData.Weekdays, DayOfWeek.Friday]
        };

        AssertHasError(WithFirstService(service), "TowardSuoiTien has 2 services on Friday");
    }

    [Fact]
    public void Validate_RejectsLastTrainArrivingAfterMidnight()
    {
        // Ga xa nhất cách 26 phút: 23:40 + 26 = 00:06 hôm sau.
        var last = new TimeOnly(23, 40);
        var service = MetroTimetableTestData.Service(MetroTimetableTestData.Weekend, MetroDirection.TowardSuoiTien, last);
        var timetable = MetroTimetableTestData.Valid();

        AssertHasError(
            timetable with { Services = [.. timetable.Services.Take(2), service, timetable.Services[3]] },
            "before 24:00");
    }

    private static MetroTimetable WithFirstService(MetroService service)
    {
        var timetable = MetroTimetableTestData.Valid();
        return timetable with { Services = [service, .. timetable.Services.Skip(1)] };
    }

    private static void AssertHasError(MetroTimetable timetable, string fragment)
        => Assert.Contains(MetroTimetableValidator.Validate(timetable), error => error.Contains(fragment));
}
