using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class ItinerarySchedulerMetroTests
{
    private static readonly MetroDayTimetable Saturday = TestTripOrigins.MetroDay(new DateOnly(2026, 10, 10));
    private static readonly TimeOnly NineOClock = new(9, 0);

    // Nhà cách ga 13 (Đại học Quốc gia) 1.045 m.
    private static readonly ScheduleOrigin FromLinhTrung =
        new(10.8756, 106.7992, new MetroBoarding(13, 1045, false, Saturday));

    [Fact]
    public void Reschedule_Metro_FirstStopStartsAfterGettingToStationWaitingAndRiding()
    {
        // Đoạn đầu 42' (xe 4' + chờ 13' + tàu 22' + đi bộ 3'); chặng hai cùng ga, cách 145 m đường bộ: đi bộ 2'.
        var stops = new[]
        {
            new ScheduleInput(10.7758, 106.7032, 60, 0, 2, 152),
            new ScheduleInput(10.7768, 106.7032, 45, 0, 2, 250)
        };

        var slots = ItineraryScheduler.Reschedule(stops, NineOClock, TravelMode.Metro, FromLinhTrung);

        Assert.Equal(new TimeOnly(9, 42), slots[0].ScheduledTime);
        Assert.Equal(new TimeOnly(10, 44), slots[1].ScheduledTime);
        Assert.Equal(42 + 60 + 2 + 45, ItineraryScheduler.TotalMinutes(
            stops, TravelMode.Metro, FromLinhTrung, startTime: NineOClock));
    }

    [Fact]
    public void Schedule_Metro_VisitsStationsNearestToBoardingStationFirst_AndStartsNearEachStation()
    {
        // Xếp hạng: [0] ga 1, [1] ga 3 (cách ga 300 m), [2] ga 2, [3] ga 3 (cách ga 100 m).
        var ranked = RankedAcrossThreeStations();

        var slots = ItineraryScheduler.Schedule(ranked, NineOClock, 12, TravelMode.Metro, 1_000_000m, FromLinhTrung);

        // Lên tàu ở ga 13: ga 3 gần nhất, rồi ga 2, rồi ga 1. Trong ga 3 bắt đầu từ địa điểm sát ga.
        Assert.Equal([3, 1, 2, 0], slots.Select(slot => slot.SourceIndex));
        Assert.Equal(slots.Select(slot => slot.ScheduledTime).Order(), slots.Select(slot => slot.ScheduledTime));
    }

    [Fact]
    public void Schedule_Metro_BoardingAtTheOtherEndOfTheLine_ReversesStationOrder()
    {
        var fromBenThanh = new ScheduleOrigin(10.7705, 106.6967, new MetroBoarding(1, 0, true, Saturday));

        var slots = ItineraryScheduler.Schedule(
            RankedAcrossThreeStations(), NineOClock, 12, TravelMode.Metro, 1_000_000m, fromBenThanh);

        Assert.Equal([0, 2, 3, 1], slots.Select(slot => slot.SourceIndex));
    }

    [Fact]
    public void Schedule_Metro_CountsWaitAndRideIntoFreeTime()
    {
        var checkIn = new[] { new ScheduleInput(10.7758, 106.7032, 45, 0, 2, 152) };

        // 42' đi + 45' tham quan = 87': không vừa 1 giờ, vừa 2 giờ.
        Assert.Empty(ItineraryScheduler.Schedule(checkIn, NineOClock, 1, TravelMode.Metro, 1_000_000m, FromLinhTrung));
        Assert.Equal(
            new TimeOnly(9, 42),
            Assert.Single(ItineraryScheduler.Schedule(checkIn, NineOClock, 2, TravelMode.Metro, 1_000_000m, FromLinhTrung))
                .ScheduledTime);
    }

    [Fact]
    public void Metro_WithoutBoardingInfo_SchedulesExactlyLikeAuto()
    {
        var origin = new ScheduleOrigin(10.8756, 106.7992);
        var stops = RankedAcrossThreeStations();

        var metro = ItineraryScheduler.Schedule(stops, NineOClock, 12, TravelMode.Metro, 1_000_000m, origin);
        var auto = ItineraryScheduler.Schedule(stops, NineOClock, 12, TravelMode.Auto, 1_000_000m, origin);

        Assert.Equal(auto, metro);
    }

    [Fact]
    public void OtherModes_IgnoreBoardingInfoAndStations()
    {
        var plainOrigin = new ScheduleOrigin(FromLinhTrung.Latitude, FromLinhTrung.Longitude);
        var stops = RankedAcrossThreeStations();

        foreach (var mode in new[] { TravelMode.Auto, TravelMode.Walking, TravelMode.Motorbike })
        {
            Assert.Equal(
                ItineraryScheduler.Schedule(stops, NineOClock, 12, mode, 1_000_000m, plainOrigin),
                ItineraryScheduler.Schedule(stops, NineOClock, 12, mode, 1_000_000m, FromLinhTrung));
        }
    }

    private static ScheduleInput[] RankedAcrossThreeStations() =>
    [
        new(10.7700, 106.6970, 30, 0, 1, 80),
        new(10.7840, 106.7080, 30, 0, 3, 300),
        new(10.7750, 106.7020, 30, 0, 2, 50),
        new(10.7820, 106.7080, 30, 0, 3, 100)
    ];
}
