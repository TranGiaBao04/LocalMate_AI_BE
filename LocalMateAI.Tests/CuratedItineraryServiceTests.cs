using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Tests;

public sealed class CuratedItineraryServiceTests
{
    [Fact]
    public async Task GetCuratedItinerariesAsync_FillsStationNameFromFirstPlace()
    {
        var firstPlace = Guid.NewGuid();
        var otherPlace = Guid.NewGuid();
        var itinerary = new CuratedItineraryResponse(
            Guid.NewGuid(), "Mẫu", null, null, 120, 0, 100000,
            [new(otherPlace, "B", 2), new(firstPlace, "A", 1)]);
        var geo = new FakeGeoService(firstPlace, "Bến Thành");
        var service = new CuratedItineraryService(new FakeRepository(itinerary), geo);

        var result = await service.GetCuratedItinerariesAsync();

        Assert.Equal("Bến Thành", Assert.Single(result).StationName);
    }

    [Fact]
    public async Task GetCuratedItinerariesAsync_LeavesStationNameNullWhenNoStationFound()
    {
        var itinerary = new CuratedItineraryResponse(
            Guid.NewGuid(), "Mẫu", null, null, 120, 0, 100000, [new(Guid.NewGuid(), "A", 1)]);
        var service = new CuratedItineraryService(new FakeRepository(itinerary), new FakeGeoService(Guid.Empty, null));

        var result = await service.GetCuratedItinerariesAsync();

        Assert.Null(Assert.Single(result).StationName);
    }

    private sealed class FakeRepository(CuratedItineraryResponse itinerary) : ICuratedItineraryRepository
    {
        public Task<IReadOnlyList<CuratedItineraryResponse>> GetAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<CuratedItineraryResponse>>([itinerary]);

        public Task<CuratedItineraryForApplyReadModel?> GetForApplyAsync(
            Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<CuratedItineraryForApplyReadModel?>(null);
    }

    private sealed class FakeGeoService(Guid expectedPlaceId, string? stationName) : IGeoService
    {
        public Task<NearestStationResult?> FindNearestStationAsync(
            double latitude, double longitude, CancellationToken cancellationToken = default)
            => Task.FromResult<NearestStationResult?>(null);

        public Task<NearestStationResult?> FindNearestStationForPlaceAsync(
            Guid placeId, CancellationToken cancellationToken = default)
            => Task.FromResult(stationName is not null && placeId == expectedPlaceId
                ? new NearestStationResult(Guid.NewGuid(), stationName, 0, 0, 100)
                : null);
    }
}
