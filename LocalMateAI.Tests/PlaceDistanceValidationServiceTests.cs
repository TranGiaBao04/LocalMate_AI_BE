using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using NetTopologySuite.Geometries;
using Xunit;

namespace LocalMateAI.Tests;

public sealed class PlaceDistanceValidationServiceTests
{
    private static readonly Guid Station1Id = Guid.NewGuid();

    [Fact]
    public async Task ValidateDistance_DistanceWithin1500m_ReturnsNoWarning()
    {
        var stationRepo = new FakeStationRepository(stationDistance: 800.0);
        var geoService = new FakeGeoService(stationRepo);
        var placeRepo = new FakePlaceRepository();
        var coordService = new CoordinatesValidationService();

        var service = new PlaceDistanceValidationService(stationRepo, geoService, placeRepo, coordService);
        var request = new ValidatePlaceDistanceRequest(10.7769, 106.7009);

        var result = await service.ValidateDistanceAsync(request);

        Assert.NotNull(result);
        Assert.True(result.IsWithinThreshold);
        Assert.False(result.HasWarning);
        Assert.Null(result.WarningMessage);
        Assert.Equal(800.0, result.DistanceMeters);
    }

    [Fact]
    public async Task ValidateDistance_DistanceExceeds1500m_ReturnsWarning()
    {
        var stationRepo = new FakeStationRepository(stationDistance: 2350.0);
        var geoService = new FakeGeoService(stationRepo);
        var placeRepo = new FakePlaceRepository();
        var coordService = new CoordinatesValidationService();

        var service = new PlaceDistanceValidationService(stationRepo, geoService, placeRepo, coordService);
        var request = new ValidatePlaceDistanceRequest(10.7769, 106.7009);

        var result = await service.ValidateDistanceAsync(request);

        Assert.NotNull(result);
        Assert.False(result.IsWithinThreshold);
        Assert.True(result.HasWarning);
        Assert.NotNull(result.WarningMessage);
        Assert.Contains("vượt quá bán kính phục vụ khuyến nghị 1.5 km", result.WarningMessage);
        Assert.Equal(2350.0, result.DistanceMeters);
    }

    [Fact]
    public async Task ValidateDistance_SpecifiedStationId_CallsGetDistanceToStation()
    {
        var stationRepo = new FakeStationRepository(stationDistance: 1200.0);
        var geoService = new FakeGeoService(stationRepo);
        var placeRepo = new FakePlaceRepository();
        var coordService = new CoordinatesValidationService();

        var service = new PlaceDistanceValidationService(stationRepo, geoService, placeRepo, coordService);
        var request = new ValidatePlaceDistanceRequest(10.7769, 106.7009, Station1Id);

        var result = await service.ValidateDistanceAsync(request);

        Assert.NotNull(result);
        Assert.Equal(Station1Id, result.StationId);
        Assert.True(result.IsWithinThreshold);
        Assert.False(result.HasWarning);
    }

    [Fact]
    public async Task ValidateDistance_InvalidHcmcCoordinates_ReturnsNull()
    {
        var stationRepo = new FakeStationRepository(stationDistance: 500.0);
        var geoService = new FakeGeoService(stationRepo);
        var placeRepo = new FakePlaceRepository();
        var coordService = new CoordinatesValidationService();

        var service = new PlaceDistanceValidationService(stationRepo, geoService, placeRepo, coordService);
        var request = new ValidatePlaceDistanceRequest(21.0285, 105.8542);

        var result = await service.ValidateDistanceAsync(request);

        Assert.Null(result);
    }

    [Fact]
    public async Task ValidatePlaceDistance_ExistingPlace_ReturnsValidationResult()
    {
        var placeId = Guid.NewGuid();
        var stationRepo = new FakeStationRepository(stationDistance: 600.0);
        var geoService = new FakeGeoService(stationRepo);
        var placeRepo = new FakePlaceRepository();
        placeRepo.Locations[placeId] = new Point(106.7009, 10.7769) { SRID = 4326 };

        var coordService = new CoordinatesValidationService();
        var service = new PlaceDistanceValidationService(stationRepo, geoService, placeRepo, coordService);

        var result = await service.ValidatePlaceDistanceAsync(placeId);

        Assert.NotNull(result);
        Assert.Equal(600.0, result.DistanceMeters);
        Assert.True(result.IsWithinThreshold);
    }

    [Fact]
    public async Task ValidatePlaceDistance_NonExistentPlace_ReturnsNull()
    {
        var stationRepo = new FakeStationRepository(stationDistance: 600.0);
        var geoService = new FakeGeoService(stationRepo);
        var placeRepo = new FakePlaceRepository();
        var coordService = new CoordinatesValidationService();
        var service = new PlaceDistanceValidationService(stationRepo, geoService, placeRepo, coordService);

        var result = await service.ValidatePlaceDistanceAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    private sealed class FakeStationRepository(double stationDistance) : IMetroStationRepository
    {
        public Task<NearestStationResult?> FindNearestAsync(
            double latitude, double longitude, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<NearestStationResult?>(new NearestStationResult(
                Station1Id,
                "Ga Bến Thành",
                10.7721,
                106.6983,
                stationDistance));
        }

        public Task<NearestStationResult?> GetDistanceToStationAsync(
            Guid stationId, double latitude, double longitude, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<NearestStationResult?>(new NearestStationResult(
                stationId,
                "Ga Bến Thành",
                10.7721,
                106.6983,
                stationDistance));
        }

        public Task<IReadOnlyList<MetroStationSummaryResponse>> GetAllAsync(
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<MetroStationSummaryResponse>>([]);
        }
    }

    private sealed class FakeGeoService(IMetroStationRepository stationRepository) : IGeoService
    {
        public Task<NearestStationResult?> FindNearestStationAsync(
            double latitude, double longitude, CancellationToken cancellationToken = default)
            => stationRepository.FindNearestAsync(latitude, longitude, cancellationToken);

        public Task<NearestStationResult?> FindNearestStationForPlaceAsync(
            Guid placeId, CancellationToken cancellationToken = default)
            => Task.FromResult<NearestStationResult?>(null);
    }

    private sealed class FakePlaceRepository : IPlaceRepository
    {
        public Dictionary<Guid, Point> Locations { get; } = [];

        public Task<Point?> GetLocationAsync(Guid placeId, CancellationToken cancellationToken = default)
        {
            Locations.TryGetValue(placeId, out var point);
            return Task.FromResult(point);
        }

        public Task AddAsync(Domain.Entities.Place place, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<DeletePlacePersistenceResult> DeleteForAdminAsync(Guid placeId, CancellationToken cancellationToken = default) => Task.FromResult(DeletePlacePersistenceResult.Deleted);
        public Task<IReadOnlyList<AdminPlaceResponse>> GetAllForAdminAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AdminPlaceResponse>>([]);
        public Task<AdminPlaceResponse?> GetAdminByIdAsync(Guid placeId, CancellationToken cancellationToken = default) => Task.FromResult<AdminPlaceResponse?>(null);
        public Task<Domain.Entities.Place?> GetByIdAsync(Guid placeId, CancellationToken cancellationToken = default) => Task.FromResult<Domain.Entities.Place?>(null);
        public Task<PagedResult<AdminPlaceResponse>> GetPagedForAdminAsync(AdminPlaceQuery query, CancellationToken cancellationToken = default) => Task.FromResult(new PagedResult<AdminPlaceResponse>([], 1, 10, 0, 0));
        public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<bool> TrySaveChangesAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);

        public Task<IReadOnlyList<MetroClusterPlaceReadModel>> GetMetroClusterPlacesAsync(double radiusMeters, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<MetroClusterPlaceReadModel>>([]);

        public Task<IReadOnlyList<PlaceSummaryResponse>> GetActiveWithinRadiusAsync(double latitude, double longitude, double radiusMeters, Domain.Enums.PlaceCategory? category, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<PlaceSummaryResponse>>([]);

        public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetPlaceTagIdsByPlaceIdsAsync(IReadOnlyList<Guid> placeIds, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>>(new Dictionary<Guid, IReadOnlyList<Guid>>());

        public Task<PlaceReadModel?> GetActiveByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<PlaceReadModel?>(null);
    }
}
