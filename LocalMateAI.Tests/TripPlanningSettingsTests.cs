using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class TripPlanningSettingsTests
{
    [Fact]
    public async Task LoadAsync_WithNothingStored_EqualsDefault()
    {
        // Chặn lệch giữa mặc định trong SystemSettingDefinitions và TripPlanningSettings.Default.
        var loaded = await TripPlanningSettings.LoadAsync(new FakeSystemSettingProvider());

        Assert.Equal(TripPlanningSettings.Default, loaded);
    }

    [Fact]
    public async Task LoadAsync_ReadsEveryKey()
    {
        var provider = new FakeSystemSettingProvider()
            .Set(SystemSettingKeys.CafeVisitMinutes, 120)
            .Set(SystemSettingKeys.FoodVisitMinutes, 80)
            .Set(SystemSettingKeys.CultureVisitMinutes, 100)
            .Set(SystemSettingKeys.CheckInVisitMinutes, 30)
            .Set(SystemSettingKeys.WalkingSpeedKmH, 3.5m)
            .Set(SystemSettingKeys.MotorbikeSpeedKmH, 18m)
            .Set(SystemSettingKeys.RoadDetourFactor, 1.5m)
            .Set(SystemSettingKeys.AutoWalkingMaxMeters, 300);

        var loaded = await TripPlanningSettings.LoadAsync(provider);

        Assert.Equal(new TripPlanningSettings(120, 80, 100, 30, 3.5, 18, 1.5, 300), loaded);
    }

    [Fact]
    public void VisitMinutes_FollowSettings()
    {
        var settings = TripPlanningSettings.Default with { CafeVisitMinutes = 120 };

        Assert.Equal(60, ItineraryScheduler.VisitMinutesFor("Cafe"));
        Assert.Equal(120, ItineraryScheduler.VisitMinutesFor("Cafe", settings));
        Assert.Equal(ItineraryScheduler.DefaultVisitMinutes, ItineraryScheduler.VisitMinutesFor("Unknown", settings));
    }

    [Fact]
    public void Schedule_ShorterVisits_FitMoreStops()
    {
        // 4 quán cafe cùng toạ độ: mỗi đoạn đi 1 phút. 3 giờ: 60 phút/quán ⇒ 2 quán; 45 phút/quán ⇒ 3 quán.
        var candidates = Enumerable.Range(0, 4).Select(_ => Cafe()).ToList();

        int StopsWith(TripPlanningSettings settings) => ItineraryScheduler.Schedule(
            candidates.Select(candidate => ItineraryScheduler.ToScheduleInput(candidate, settings)).ToList(),
            new TimeOnly(9, 0), 3, TravelMode.Auto, 1_000_000m, new ScheduleOrigin(10.77, 106.69), settings).Count;

        Assert.Equal(2, StopsWith(TripPlanningSettings.Default));
        Assert.Equal(3, StopsWith(TripPlanningSettings.Default with { CafeVisitMinutes = 45 }));
    }

    [Fact]
    public void TravelEstimates_FollowSettings()
    {
        var slowWalk = TripPlanningSettings.Default with { WalkingSpeedKmH = 2.4 };
        Assert.Equal(30, TravelTimeEstimator.WalkingMinutes(2.4));
        Assert.Equal(60, TravelTimeEstimator.WalkingMinutes(2.4, slowWalk));

        var shortWalkThreshold = TripPlanningSettings.Default with { AutoWalkingMaxMeters = 300 };
        Assert.Equal(TravelTimeEstimator.WalkingMinutes(0.5), TravelTimeEstimator.EstimateMinutes(0.5, TravelMode.Auto));
        Assert.Equal(TravelTimeEstimator.MotorbikeMinutes(0.5),
            TravelTimeEstimator.EstimateMinutes(0.5, TravelMode.Auto, shortWalkThreshold));

        var haversine = TravelTimeEstimator.HaversineKm(10.77, 106.69, 10.78, 106.69);
        var detour = TripPlanningSettings.Default with { RoadDetourFactor = 1.5 };
        Assert.Equal(Math.Round(haversine * 1.3, 2), TravelTimeEstimator.RoadDistanceKm(10.77, 106.69, 10.78, 106.69));
        Assert.Equal(Math.Round(haversine * 1.5, 2),
            TravelTimeEstimator.RoadDistanceKm(10.77, 106.69, 10.78, 106.69, detour));
    }

    private static PlaceCandidateDto Cafe() => new(
        Guid.NewGuid(), "Cafe", "Địa chỉ", 10.77, 106.69, "Cafe", 0, 0, null, Guid.NewGuid(), "Ga", 1, 100);
}
