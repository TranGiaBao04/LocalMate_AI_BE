using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

public sealed class MetroClusterMatchingServiceTests
{
    // 5 ga liên tiếp, mỗi ga có 1 địa điểm.
    private static readonly IReadOnlyList<MetroStationSummaryResponse> Stations = Enumerable.Range(1, 5)
        .Select(order => new MetroStationSummaryResponse(Guid.NewGuid(), $"Ga {order}", order, 10.77, 106.70))
        .ToList();

    [Fact]
    public async Task PassesClusterRadiusFromSettings_ToRepository()
    {
        var places = new FakePlaceRepository();

        await CreateService(places, new FakeSystemSettingProvider()).GetCandidatesAsync(StationId(3));
        Assert.Equal(800, places.LastRadius);

        await CreateService(places,
                new FakeSystemSettingProvider().Set(SystemSettingKeys.StationClusterRadiusMeters, 1_000))
            .GetCandidatesAsync(StationId(3));
        Assert.Equal(1_000, places.LastRadius);
    }

    [Fact]
    public async Task DefaultWindow_TakesOneAdjacentStationEachSide()
    {
        var candidates = await CreateService(new FakePlaceRepository(), new FakeSystemSettingProvider())
            .GetCandidatesAsync(StationId(3));

        Assert.Equal([2, 3, 4], candidates.Select(candidate => candidate.StationOrder));
    }

    [Theory]
    [InlineData(0, new[] { 3 })]
    [InlineData(2, new[] { 1, 2, 3, 4, 5 })]
    public async Task Window_FollowsAdminSetting(int window, int[] expectedOrders)
    {
        var settings = new FakeSystemSettingProvider().Set(SystemSettingKeys.AdjacentStationWindow, window);

        var candidates = await CreateService(new FakePlaceRepository(), settings).GetCandidatesAsync(StationId(3));

        Assert.Equal(expectedOrders, candidates.Select(candidate => candidate.StationOrder));
    }

    [Fact]
    public async Task UnknownOriginStation_ReturnsEmpty_WithoutReadingSettings()
    {
        var settings = new FakeSystemSettingProvider();
        var places = new FakePlaceRepository();

        var candidates = await CreateService(places, settings).GetCandidatesAsync(Guid.NewGuid());

        Assert.Empty(candidates);
        Assert.Empty(settings.RequestedKeys);
        Assert.Null(places.LastRadius);
    }

    [Fact]
    public async Task AttachesOpeningHours_EmptyWhenThePlaceHasNone()
    {
        var places = new FakePlaceRepository();
        var hours = new FakePlaceOpeningHourRepository();
        var withoutHours = await CreateService(places, new FakeSystemSettingProvider(), hours)
            .GetCandidatesAsync(StationId(3));
        Assert.All(withoutHours, candidate => Assert.Empty(candidate.OpeningHours!));

        // Mỗi lần gọi repository giả sinh PlaceId mới nên gắn giờ theo đúng PlaceId của lần gọi thứ hai.
        places.FixedPlaceIds = withoutHours.Select(candidate => candidate.PlaceId).ToList();
        hours.Hours[withoutHours[0].PlaceId] = TestOpeningHours.EveryDay(8, 17);
        var candidates = await CreateService(places, new FakeSystemSettingProvider(), hours)
            .GetCandidatesAsync(StationId(3));

        Assert.Equal(7, candidates.Single(c => c.PlaceId == withoutHours[0].PlaceId).OpeningHours!.Count);
        Assert.All(candidates.Where(c => c.PlaceId != withoutHours[0].PlaceId), c => Assert.Empty(c.OpeningHours!));
    }

    private static Guid StationId(int order) => Stations.Single(station => station.Order == order).Id;

    private static MetroClusterMatchingService CreateService(FakePlaceRepository places,
        FakeSystemSettingProvider settings, FakePlaceOpeningHourRepository? hours = null) =>
        new(places, new FakeStationRepository(), settings, hours ?? new FakePlaceOpeningHourRepository());

    private sealed class FakeStationRepository : IMetroStationRepository
    {
        public Task<NearestStationResult?> FindNearestAsync(double latitude, double longitude,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<NearestStationResult?> GetDistanceToStationAsync(
            Guid stationId, double latitude, double longitude, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MetroStationSummaryResponse>> GetAllAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(Stations);
    }

    private sealed class FakePlaceRepository : IPlaceRepository
    {
        public Task<IReadOnlyList<Guid>> GetTagIdsAsync(Guid placeId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task AddTagAsync(Guid placeId, Guid tagId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> RemoveTagAsync(Guid placeId, Guid tagId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<DuplicatePlaceCandidate>> FindNearbyPlacesAsync(double latitude, double longitude, double radiusMeters, CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public double? LastRadius { get; private set; }

        // Đặt sẵn PlaceId theo thứ tự ga (ga 2, 3, 4 khi gọi ga 3 với cửa sổ ±1); null = sinh mới mỗi lần.
        public IReadOnlyList<Guid>? FixedPlaceIds { get; set; }

        public Task<IReadOnlyList<MetroClusterPlaceReadModel>> GetMetroClusterPlacesAsync(double radiusMeters,
            CancellationToken cancellationToken = default)
        {
            LastRadius = radiusMeters;
            IReadOnlyList<MetroClusterPlaceReadModel> rows = Stations
                .Select(station => new MetroClusterPlaceReadModel(station.Id, station.Name, station.Order, 10.77,
                    106.70, PlaceIdFor(station.Order), $"Địa điểm {station.Order}", "Địa chỉ", 10.77, 106.70, "Cafe", 0,
                    50_000, null, 100))
                .ToList();
            return Task.FromResult(rows);
        }

        private Guid PlaceIdFor(int stationOrder) =>
            FixedPlaceIds is { } ids && stationOrder >= 2 && stationOrder - 2 < ids.Count
                ? ids[stationOrder - 2]
                : Guid.NewGuid();

        public Task<Point?> GetLocationAsync(Guid placeId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PlaceSummaryResponse>> GetActiveWithinRadiusAsync(double latitude,
            double longitude, double radiusMeters, PlaceCategory? category,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetPlaceTagIdsByPlaceIdsAsync(
            IReadOnlyList<Guid> placeIds, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AdminPlaceResponse>> GetAllForAdminAsync(
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PagedResult<AdminPlaceResponse>> GetPagedForAdminAsync(
            AdminPlaceQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdminPlaceResponse?> GetAdminByIdAsync(Guid placeId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<Place?> GetByIdAsync(Guid placeId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddAsync(Place place, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<DeletePlacePersistenceResult> DeleteForAdminAsync(Guid placeId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PlaceReadModel?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
