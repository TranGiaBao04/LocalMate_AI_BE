using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Tests;

public sealed class StationSuggestionServiceTests
{
    // 6 ga nằm trên một đường thẳng, ga sau xa hơn ga trước về phía bắc (~1,1 km mỗi ga).
    private static readonly IReadOnlyList<MetroStationSummaryResponse> Stations = Enumerable.Range(1, 6)
        .Select(order => new MetroStationSummaryResponse(Guid.NewGuid(), $"Ga {order}", order, 10.70 + order * 0.01, 106.70))
        .ToList();

    [Fact]
    public async Task Suggest_ReturnsNearestStationsWithEnoughPlaces_CountingAdjacentStations()
    {
        // Địa điểm của riêng từng ga: 1→5, 2→6, 3→1. Tính cả ga kề (±1): ga 1 = 11, ga 2 = 12, ga 3 = 7, ga 4 = 1.
        var places = Places((1, 5), (2, 6), (3, 1));

        var suggestions = await Service(places).SuggestAsync(OriginAt(station: 6));

        Assert.Equal(
            [(3, "Ga 3", 7), (2, "Ga 2", 12), (1, "Ga 1", 11)],
            suggestions.Select(station => (station.Order, station.Name, station.PlaceCount)));
    }

    [Fact]
    public async Task Suggest_NeverReturnsTheAnchorStationItself()
    {
        var places = Places((1, 5), (2, 6), (3, 1));

        var suggestions = await Service(places).SuggestAsync(OriginAt(station: 2));

        Assert.DoesNotContain(suggestions, station => station.Order == 2);
        Assert.Equal([1, 3], suggestions.Select(station => station.Order).Order());
    }

    [Fact]
    public async Task Suggest_SortsByDistanceFromWhereTheUserStarts_NotFromTheAnchor()
    {
        var places = Places((1, 5), (2, 6), (3, 1));

        // Xuất phát cạnh ga 1 nhưng muốn chơi quanh ga 6: gợi ý phải bắt đầu từ ga gần người dùng.
        var origin = TestTripOrigins.At(boardingStation: Stations[0], anchorStation: Stations[5]);

        var suggestions = await Service(places).SuggestAsync(origin);

        Assert.Equal([1, 2, 3], suggestions.Select(station => station.Order));
    }

    [Fact]
    public async Task Suggest_ReturnsAtMostThreeStations()
    {
        var places = Places((1, 9), (2, 9), (3, 9), (4, 9), (5, 9));

        var suggestions = await Service(places).SuggestAsync(OriginAt(station: 6));

        Assert.Equal(StationSuggestionService.MaxSuggestions, suggestions.Count);
        Assert.Equal([5, 4, 3], suggestions.Select(station => station.Order));
    }

    [Fact]
    public async Task Suggest_ThresholdFollowsAdminSetting()
    {
        var settings = new FakeSystemSettingProvider().Set(SystemSettingKeys.MinActivePlacesPerStation, 12);

        var suggestions = await Service(Places((1, 5), (2, 6), (3, 1)), settings).SuggestAsync(OriginAt(station: 6));

        Assert.Equal([2], suggestions.Select(station => station.Order));
    }

    [Fact]
    public async Task Suggest_AdjacentWindowAndRadiusFollowAdminSettings()
    {
        var settings = new FakeSystemSettingProvider()
            .Set(SystemSettingKeys.AdjacentStationWindow, 0)
            .Set(SystemSettingKeys.StationClusterRadiusMeters, 500);
        var places = Places((1, 5), (2, 6), (3, 1));

        var suggestions = await Service(places, settings).SuggestAsync(OriginAt(station: 6));

        // Không cộng ga kề: chỉ ga 1 (5) và ga 2 (6) đạt ngưỡng 5.
        Assert.Equal([(2, 6), (1, 5)], suggestions.Select(station => (station.Order, station.PlaceCount)));
        Assert.Equal(500, places.LastRadius);
    }

    [Fact]
    public async Task Suggest_NoStationHasEnoughPlaces_ReturnsEmpty() =>
        Assert.Empty(await Service(Places((1, 1), (4, 2))).SuggestAsync(OriginAt(station: 6)));

    private static StationSuggestionService Service(StubPlaceRepository places, FakeSystemSettingProvider? settings = null) =>
        new(places, new StubStationRepository(Stations), settings ?? new FakeSystemSettingProvider());

    private static LocalMateAI.Application.DTOs.Trips.TripOriginResolution OriginAt(int station) =>
        TestTripOrigins.At(boardingStation: Stations[station - 1]);

    private static StubPlaceRepository Places(params (int StationOrder, int Count)[] counts) => new()
    {
        ClusterPlaces = counts
            .SelectMany(entry => Enumerable.Range(0, entry.Count).Select(_ =>
            {
                var station = Stations[entry.StationOrder - 1];
                return new MetroClusterPlaceReadModel(
                    station.Id, station.Name, station.Order, station.Latitude, station.Longitude,
                    Guid.NewGuid(), "Địa điểm", "Địa chỉ", station.Latitude, station.Longitude, "Cafe",
                    0, 50_000, null, 100);
            }))
            .ToList()
    };
}
