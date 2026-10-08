using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class TripLegPlannerTests
{
    private static readonly TripPlanningSettings Settings = TripPlanningSettings.Default;
    private static readonly MetroDayTimetable Saturday = TestTripOrigins.MetroDay(new DateOnly(2026, 10, 10));
    private static readonly MetroDayTimetable Wednesday = TestTripOrigins.MetroDay(new DateOnly(2026, 10, 7));

    // Trục toạ độ test: 0,001° vĩ độ ≈ 111 m chim bay. Đường bộ (×1,3) và số phút:
    //   0,001° → 0,14 km: đi bộ 2', xe máy 1'      0,004° → 0,58 km: đi bộ 8'
    //   0,010° → 1,45 km: đi bộ 19', xe máy 4'     0,020° → 2,89 km: đi bộ 37', xe máy 8'
    private const double BaseLatitude = 10.7700;
    private const double Longitude = 106.7000;

    private static ScheduleInput Stop(double degreesNorth, int? stationOrder, double distanceFromStationMeters = 152) =>
        new(BaseLatitude + degreesNorth, Longitude, 60, 0, stationOrder, distanceFromStationMeters);

    private static ScheduleOrigin Origin(MetroBoarding? metro) => new(BaseLatitude, Longitude, metro);

    // ---------- Từ điểm xuất phát tới chặng đầu ----------

    [Fact]
    public void FromOrigin_FarFromDestinationStation_RidesTrain_AfterGettingToBoardingStation()
    {
        // Nhà cách ga 13 1.045 m (đường bộ 1,36 km > 700 m ⇒ đi xe 4'), tới ga 09:04, chờ 13', tàu 22' qua 11 ga,
        // rồi đi bộ 152 m chim bay (0,20 km) mất 3'.
        var origin = new ScheduleOrigin(10.8756, 106.7992, new MetroBoarding(13, 1045, false, Saturday));

        var leg = TripLegPlanner.FromOrigin(origin, Stop(0, 2), Minute(9, 0), TravelMode.Metro, Settings);

        Assert.Equal(TravelMode.Metro, leg.Mode);
        Assert.Equal(42, leg.TotalMinutes);
        Assert.Equal(new MetroLegDetail(TravelMode.Motorbike, 4, 13, 2, 13, 22, 11, 3), leg.Metro);
        Assert.Null(leg.Fallback);
    }

    [Fact]
    public void FromOrigin_StartingAtStation_HasNoTripToStation()
    {
        // Đứng sẵn ở ga 13 lúc 09:00: chuyến kế tiếp 09:02.
        var origin = new ScheduleOrigin(10.8664, 106.8013, new MetroBoarding(13, 0, true, Saturday));

        var leg = TripLegPlanner.FromOrigin(origin, Stop(0, 2), Minute(9, 0), TravelMode.Metro, Settings);

        Assert.Equal(27, leg.TotalMinutes);
        Assert.Equal(new MetroLegDetail(null, 0, 13, 2, 2, 22, 11, 3), leg.Metro);
    }

    [Fact]
    public void FromOrigin_FirstStopBelongsToBoardingStation_GoesDirectly()
    {
        var near = TripLegPlanner.FromOrigin(
            Origin(new MetroBoarding(2, 300, false, Saturday)), Stop(0.001, 2), Minute(9, 0), TravelMode.Metro, Settings);
        var far = TripLegPlanner.FromOrigin(
            Origin(new MetroBoarding(2, 300, false, Saturday)), Stop(0.010, 2), Minute(9, 0), TravelMode.Metro, Settings);

        // Cùng ga thì không lên tàu: gần đi bộ, xa đi xe (xe máy/Grab).
        Assert.Equal(new TripLegPlan(TravelMode.Walking, 2), near);
        Assert.Equal(new TripLegPlan(TravelMode.Motorbike, 4), far);
    }

    [Fact]
    public void FromOrigin_NeighbourStationWithinWalkingDistance_WalksInsteadOfRiding()
    {
        // Khác ga nhưng chỉ cách 0,58 km đường bộ (≤ 700 m): đi bộ thẳng, không lên tàu.
        var leg = TripLegPlanner.FromOrigin(
            Origin(new MetroBoarding(1, 300, false, Saturday)), Stop(0.004, 2), Minute(9, 0), TravelMode.Metro, Settings);

        Assert.Equal(new TripLegPlan(TravelMode.Walking, 8), leg);
    }

    [Fact]
    public void FromOrigin_StartingAtStation_WithoutTrain_AlwaysWalks()
    {
        // Xuất phát từ ga (không có xe): 1,45 km cùng ga vẫn tính đi bộ 19', không phải xe máy.
        var leg = TripLegPlanner.FromOrigin(
            Origin(new MetroBoarding(2, 0, true, Saturday)), Stop(0.010, 2), Minute(9, 0), TravelMode.Metro, Settings);

        Assert.Equal(new TripLegPlan(TravelMode.Walking, 19), leg);
    }

    [Fact]
    public void BetweenStops_MetroWithoutTimetable_IsEstimatedLikeAuto()
    {
        var leg = TripLegPlanner.BetweenStops(
            Stop(0, 2), Stop(0.020, 5), Minute(10, 0), TravelMode.Metro, timetable: null, Settings);

        Assert.Equal(new TripLegPlan(TravelMode.Motorbike, 8), leg);
    }

    [Fact]
    public void FromOrigin_MetroWithoutBoardingInfo_IsEstimatedLikeAuto()
    {
        var leg = TripLegPlanner.FromOrigin(Origin(null), Stop(0.020, 5), Minute(9, 0), TravelMode.Metro, Settings);

        Assert.Equal(new TripLegPlan(TravelMode.Motorbike, 8), leg);
    }

    // ---------- Giữa hai chặng ----------

    [Fact]
    public void BetweenStops_DifferentStationsFarApart_WalksToStationThenRides()
    {
        // Rời chặng ở ga 2 lúc 10:43, đi bộ về ga 3' (tới 10:46), tàu qua ga 2 lúc 10:47 ⇒ chờ 1',
        // ngồi tàu 6' tới ga 5, đi bộ 300 m chim bay (0,39 km) mất 5'.
        var timetable = Saturday;

        var leg = TripLegPlanner.BetweenStops(
            Stop(0, 2), Stop(0.020, 5, 300), Minute(10, 43), TravelMode.Metro, timetable, Settings);

        Assert.Equal(TravelMode.Metro, leg.Mode);
        Assert.Equal(15, leg.TotalMinutes);
        Assert.Equal(new MetroLegDetail(TravelMode.Walking, 3, 2, 5, 1, 6, 3, 5), leg.Metro);
    }

    [Fact]
    public void BetweenStops_WithoutTrain_AlwaysWalks()
    {
        var timetable = Saturday;

        var neighbourStationNearby = TripLegPlanner.BetweenStops(
            Stop(0.001, 1), Stop(0.004, 2), Minute(10, 0), TravelMode.Metro, timetable, Settings);
        var sameStationFarApart = TripLegPlanner.BetweenStops(
            Stop(0, 2), Stop(0.010, 2), Minute(10, 0), TravelMode.Metro, timetable, Settings);

        Assert.Equal(new TripLegPlan(TravelMode.Walking, 6), neighbourStationNearby); // 0,43 km
        Assert.Equal(new TripLegPlan(TravelMode.Walking, 19), sameStationFarApart);  // 1,45 km, không có xe
    }

    [Fact]
    public void BetweenStops_AfterLastTrain_FallsBackToMotorbike()
    {
        // Thứ 4, 22:30: chuyến cuối về phía Suối Tiên đã qua ga 2 lúc 22:02.
        var timetable = Wednesday;

        var leg = TripLegPlanner.BetweenStops(
            Stop(0, 2), Stop(0.020, 5, 300), Minute(22, 30), TravelMode.Metro, timetable, Settings);

        Assert.Equal(new TripLegPlan(TravelMode.Motorbike, 8, Fallback: TripLegPlanner.MetroUnavailable), leg);
    }

    [Theory]
    [InlineData(TravelMode.Auto, 0.001, TravelMode.Walking, 2)]
    [InlineData(TravelMode.Auto, 0.020, TravelMode.Motorbike, 8)]
    [InlineData(TravelMode.Walking, 0.020, TravelMode.Walking, 37)]
    [InlineData(TravelMode.Motorbike, 0.001, TravelMode.Motorbike, 1)]
    public void BetweenStops_OtherModes_GoDirectly_EvenWhenTimetableIsPresent(
        TravelMode mode, double degreesNorth, TravelMode expectedMode, int expectedMinutes)
    {
        var leg = TripLegPlanner.BetweenStops(
            Stop(0, 2), Stop(degreesNorth, 5), Minute(10, 0), mode, Saturday, Settings);

        Assert.Equal(new TripLegPlan(expectedMode, expectedMinutes), leg);
    }

    private static int Minute(int hour, int minute) => hour * 60 + minute;
}
