using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class TripClockTests
{
    [Fact]
    public void StartMinutes_SameDay_ReturnsMinuteOfDay()
    {
        var starts = TripClock.StartMinutes(new TimeOnly(7, 40), [Stop(8, 0, 60), Stop(9, 15, 75)]);

        Assert.Equal([480, 555], starts);
    }

    [Fact]
    public void StartMinutes_StopAfterMidnight_AddsOneDay()
    {
        var starts = TripClock.StartMinutes(new TimeOnly(22, 30), [Stop(23, 0, 60), Stop(0, 30, 45)]);

        Assert.Equal([1380, 1470], starts);
    }

    [Fact]
    public void StartMinutes_FirstStopAfterMidnightComparedWithLeaveTime_AddsOneDay()
    {
        var starts = TripClock.StartMinutes(new TimeOnly(23, 50), [Stop(0, 10, 60)]);

        Assert.Equal([1450], starts);
    }

    [Fact]
    public void StartMinutes_KeepsCountingAfterTheDayChanged()
    {
        var starts = TripClock.StartMinutes(
            new TimeOnly(23, 0), [Stop(23, 20, 60), Stop(0, 30, 60), Stop(1, 45, 30)]);

        Assert.Equal([1400, 1470, 1545], starts);
    }

    [Fact]
    public void StartMinutes_SmallOverlap_StaysOnTheSameDay()
    {
        // Chặng sau bắt đầu trước khi chặng trước xong (dữ liệu chồng giờ), không phải qua nửa đêm.
        var starts = TripClock.StartMinutes(null, [Stop(8, 0, 90), Stop(9, 0, 60)]);

        Assert.Equal([480, 540], starts);
    }

    [Fact]
    public void StartMinutes_LeaveTimeSlightlyLaterThanFirstStop_StaysOnTheSameDay()
    {
        var starts = TripClock.StartMinutes(new TimeOnly(9, 0), [Stop(8, 20, 90)]);

        Assert.Equal([500], starts);
    }

    [Fact]
    public void StartMinutes_WithoutLeaveTime_TreatsFirstStopAsSameDay()
    {
        var starts = TripClock.StartMinutes(null, [Stop(0, 30, 60)]);

        Assert.Equal([30], starts);
    }

    [Fact]
    public void StartMinutes_NoStops_ReturnsEmpty()
    {
        Assert.Empty(TripClock.StartMinutes(new TimeOnly(8, 0), []));
    }

    private static TripStopSnapshot Stop(int hour, int minute, int durationMinutes) =>
        new(new TimeOnly(hour, minute), durationMinutes, 0m);
}
