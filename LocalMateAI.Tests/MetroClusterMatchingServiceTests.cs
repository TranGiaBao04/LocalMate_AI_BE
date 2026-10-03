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

    private static Guid StationId(int order) => Stations.Single(station => station.Order == order).Id;

    private static MetroClusterMatchingService CreateService(FakePlaceRepository places,
        FakeSystemSettingProvider settings) =>
        new(places, new FakeStationRepository(), settings);

    private sealed class FakeStationRepository : IMetroStationRepository
    {
        public Task<NearestStationResult?> FindNearestAsync(double latitude, double longitude,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<MetroStationSummaryResponse>> GetAllAsync(
            CancellationToken cancellationToken = default) => Task.FromResult(Stations);
    }

    private sealed class FakePlaceRepository : IPlaceRepository
    {
        public double? LastRadius { get; private set; }

        public Task<IReadOnlyList<MetroClusterPlaceReadModel>> GetMetroClusterPlacesAsync(double radiusMeters,
            CancellationToken cancellationToken = default)
        {
            LastRadius = radiusMeters;
            IReadOnlyList<MetroClusterPlaceReadModel> rows = Stations
                .Select(station => new MetroClusterPlaceReadModel(station.Id, station.Name, station.Order, 10.77,
                    106.70, Guid.NewGuid(), $"Địa điểm {station.Order}", "Địa chỉ", 10.77, 106.70, "Cafe", 0, 50_000,
                    null, 100))
                .ToList();
            return Task.FromResult(rows);
        }

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
