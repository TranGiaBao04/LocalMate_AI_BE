using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class ItinerarySchedulerTests
{
    private static readonly TimeOnly Start = new(8, 0);

    // 0,001° vĩ độ ≈ 111 m đường chim bay (≈ 145 m đường bộ); 0,02° ≈ 2,89 km đường bộ.
    private static ScheduleInput At(double latitude, int durationMinutes = 60) =>
        new(latitude, 106.70, durationMinutes);

    [Fact]
    public void VisitMinutesFor_MapsEachCategory_AndFallsBackToDefault()
    {
        Assert.Equal(60, ItineraryScheduler.VisitMinutesFor(nameof(PlaceCategory.Cafe)));
        Assert.Equal(75, ItineraryScheduler.VisitMinutesFor(nameof(PlaceCategory.Food)));
        Assert.Equal(90, ItineraryScheduler.VisitMinutesFor(nameof(PlaceCategory.Culture)));
        Assert.Equal(45, ItineraryScheduler.VisitMinutesFor(nameof(PlaceCategory.CheckIn)));
        Assert.Equal(ItineraryScheduler.DefaultVisitMinutes, ItineraryScheduler.VisitMinutesFor("Unknown"));
    }

    [Fact]
    public void Schedule_NoStops_ReturnsEmpty()
    {
        Assert.Empty(ItineraryScheduler.Schedule([], Start, 8, TravelMode.Auto, 1_000_000m));
    }

    [Fact]
    public void Reschedule_KeepsOrderAndDuration_AddsTravelTime()
    {
        var slots = ItineraryScheduler.Reschedule([At(10.770, 60), At(10.771, 45)], Start, TravelMode.Auto);

        Assert.Equal(2, slots.Count);
        Assert.Equal(new TimeOnly(8, 0), slots[0].ScheduledTime);
        Assert.Equal(new TimeOnly(9, 2), slots[1].ScheduledTime); // 60' tham quan + 2' đi bộ ~145 m
        Assert.Equal(45, slots[1].DurationMinutes);
        Assert.Equal([0, 1], slots.Select(slot => slot.SourceIndex));
    }

    [Fact]
    public void Reschedule_LongLeg_AutoUsesMotorbikeButWalkingModeDoesNot()
    {
        var stops = new[] { At(10.770, 30), At(10.790, 30) };

        var auto = ItineraryScheduler.Reschedule(stops, Start, TravelMode.Auto);
        var walking = ItineraryScheduler.Reschedule(stops, Start, TravelMode.Walking);

        Assert.Equal(new TimeOnly(8, 38), auto[1].ScheduledTime);    // 30' + 8' xe máy
        Assert.Equal(new TimeOnly(9, 7), walking[1].ScheduledTime);  // 30' + 37' đi bộ
    }

    [Fact]
    public void Schedule_KeepsFirstStopThenOrdersByNearest()
    {
        // Xếp theo điểm: A, B (xa), C (sát A) → đi A, C, B
        var slots = ItineraryScheduler.Schedule([At(10.770), At(10.790), At(10.771)], Start, 10, TravelMode.Auto, 1_000_000m);

        Assert.Equal([0, 2, 1], slots.Select(slot => slot.SourceIndex));
        Assert.Equal(Start, slots[0].ScheduledTime);
    }

    [Fact]
    public void Schedule_TrimsTrailingStopsThatOverflowDuration()
    {
        // Cùng vị trí: mỗi chặng 60' + 1' đi → kết thúc ở phút 60, 121, 182
        var slots = ItineraryScheduler.Schedule([At(10.770), At(10.770), At(10.770)], Start, 3, TravelMode.Auto, 1_000_000m);

        Assert.Equal(2, slots.Count);
    }

    [Fact]
    public void Schedule_FillsWithMoreStopsUntilDurationIsUsed()
    {
        var stops = Enumerable.Range(0, 8).Select(_ => At(10.770)).ToList();

        // Mỗi chặng 60' + 1' đi → kết thúc ở phút 60, 121, 182, 243, 304, 365 → 5 chặng nằm trong 6 giờ
        var slots = ItineraryScheduler.Schedule(stops, Start, 6, TravelMode.Auto, 1_000_000m);

        Assert.Equal(5, slots.Count);
    }

    [Fact]
    public void Schedule_StopsAddingWhenBudgetWouldBeExceeded()
    {
        var stops = Enumerable.Range(0, 10).Select(_ => new ScheduleInput(10.770, 106.70, 30, 100_000m)).ToList();

        var slots = ItineraryScheduler.Schedule(stops, Start, 12, TravelMode.Auto, 250_000m);

        Assert.Equal(2, slots.Count); // 3 chặng sẽ tốn 300.000 > 250.000
    }

    [Fact]
    public void Schedule_SkipsStopThatDoesNotFit_ThenTriesNextOne()
    {
        // A 60', B 300' (không vừa 3 giờ), C 30' → chọn A và C
        var slots = ItineraryScheduler.Schedule([At(10.770, 60), At(10.770, 300), At(10.770, 30)], Start, 3, TravelMode.Auto, 1_000_000m);

        Assert.Equal([0, 2], slots.Select(slot => slot.SourceIndex));
    }

    [Fact]
    public void Schedule_ReturnsEmpty_WhenEvenTheTopStopExceedsBudget()
    {
        var slots = ItineraryScheduler.Schedule([new ScheduleInput(10.770, 106.70, 60, 500_000m)], Start, 6, TravelMode.Auto, 100_000m);

        Assert.Empty(slots);
    }

    [Fact]
    public void Schedule_ReturnsEmpty_WhenNoStopFitsTheDuration()
    {
        var slots = ItineraryScheduler.Schedule([At(10.770, 90)], Start, 1, TravelMode.Auto, 1_000_000m);

        Assert.Empty(slots);
    }

    [Fact]
    public void Schedule_SkipsTopStopThatIsTooLong_AndUsesNextRankedOne()
    {
        // Hạng 1 cần 90' nhưng chỉ rảnh 1 giờ; hạng 2 cần 45' → chọn hạng 2.
        var slots = ItineraryScheduler.Schedule([At(10.770, 90), At(10.770, 45)], Start, 1, TravelMode.Auto, 1_000_000m);

        Assert.Equal([1], slots.Select(slot => slot.SourceIndex));
        Assert.Equal(Start, slots[0].ScheduledTime);
    }

    // 0,02° vĩ độ ≈ 2,89 km đường bộ → Auto đi xe máy 8 phút.
    private static readonly ScheduleOrigin FarOrigin = new(10.750, 106.70);

    [Fact]
    public void Schedule_WithOrigin_FirstStopStartsAfterTravelFromOrigin()
    {
        var withOrigin = ItineraryScheduler.Schedule([At(10.770)], Start, 6, TravelMode.Auto, 1_000_000m, FarOrigin);
        var withoutOrigin = ItineraryScheduler.Schedule([At(10.770)], Start, 6, TravelMode.Auto, 1_000_000m);

        Assert.Equal(new TimeOnly(8, 8), withOrigin[0].ScheduledTime);
        Assert.Equal(Start, withoutOrigin[0].ScheduledTime);
    }

    [Fact]
    public void Schedule_WithOrigin_TravelFromOriginCountsTowardDuration()
    {
        // 60' tham quan + 8' đi tới = 68' > 60' rảnh.
        var slots = ItineraryScheduler.Schedule([At(10.770, 60)], Start, 1, TravelMode.Auto, 1_000_000m, FarOrigin);

        Assert.Empty(slots);
    }

    [Fact]
    public void Schedule_WithOriginAtTheStop_CostsOneMinute()
    {
        var slots = ItineraryScheduler.Schedule([At(10.770)], Start, 6, TravelMode.Auto, 1_000_000m, new ScheduleOrigin(10.770, 106.70));

        Assert.Equal(new TimeOnly(8, 1), slots[0].ScheduledTime);
    }

    [Fact]
    public void Reschedule_WithOrigin_AddsTravelFromOriginBeforeFirstStop()
    {
        var slots = ItineraryScheduler.Reschedule([At(10.770, 60), At(10.771, 45)], Start, TravelMode.Auto, FarOrigin);

        Assert.Equal(new TimeOnly(8, 8), slots[0].ScheduledTime);
        Assert.Equal(new TimeOnly(9, 10), slots[1].ScheduledTime); // 8' + 60' + 2' đi bộ
    }

    [Fact]
    public void TotalMinutes_SumsVisitsAndTravel_AndIsZeroForNoStops()
    {
        Assert.Equal(0, ItineraryScheduler.TotalMinutes([], TravelMode.Auto, null));
        // 60' + 2' đi bộ ~145 m + 45' = 107'
        Assert.Equal(107, ItineraryScheduler.TotalMinutes([At(10.770, 60), At(10.771, 45)], TravelMode.Auto, null));
        Assert.Equal(115, ItineraryScheduler.TotalMinutes([At(10.770, 60), At(10.771, 45)], TravelMode.Auto, FarOrigin)); // + 8' đi tới chặng đầu
    }
}
