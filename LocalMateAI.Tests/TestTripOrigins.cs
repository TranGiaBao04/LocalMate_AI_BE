using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Metro;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Tests;

/// <summary>Dựng nhanh TripOriginResolution cho test. Mặc định: xuất phát bằng toạ độ ngay tại ga Bến Thành.</summary>
internal static class TestTripOrigins
{
    public static readonly MetroStationSummaryResponse BenThanh =
        new(Guid.NewGuid(), "Bến Thành", 1, 10.7721, 106.6980);

    public static TripOriginResolution At(
        MetroStationSummaryResponse? boardingStation = null,
        MetroStationSummaryResponse? anchorStation = null,
        bool withinServiceArea = true,
        double? startLatitude = null,
        double? startLongitude = null,
        Guid? startStationId = null,
        Guid? destinationStationId = null,
        string? serviceAreaFailure = null,
        MetroBoarding? metroBoarding = null)
    {
        var boarding = boardingStation ?? BenThanh;

        return new TripOriginResolution(
            startLatitude ?? boarding.Latitude,
            startLongitude ?? boarding.Longitude,
            new NearestStationResult(boarding.Id, boarding.Name, boarding.Latitude, boarding.Longitude, 300),
            anchorStation ?? boarding,
            startStationId,
            destinationStationId,
            serviceAreaFailure ?? (withinServiceArea ? null : TripInsufficiencyReasons.OutOfServiceArea),
            metroBoarding);
    }

    /// <summary>Lịch tàu test (ga cách nhau 2 phút; 05:00–07:00 mỗi 10 phút, sau đó mỗi 15 phút) của một ngày.</summary>
    public static MetroDayTimetable MetroDay(DateOnly date) =>
        MetroDayTimetable.For(MetroTimetableTestData.Valid(), date);

    public static StationRefDto Ref(MetroStationSummaryResponse station) => new(station.Order, station.Name);
}

internal sealed class FakeStationSuggestionService(params SuggestedStationDto[] suggestions) : IStationSuggestionService
{
    public int Calls { get; private set; }

    public Task<IReadOnlyList<SuggestedStationDto>> SuggestAsync(
        TripOriginResolution origin, CancellationToken cancellationToken = default)
    {
        Calls++;
        return Task.FromResult<IReadOnlyList<SuggestedStationDto>>(suggestions);
    }
}

/// <summary>
/// IPlaceRepository giả cho test chỉ cần dữ liệu cụm ga và tag của địa điểm; mọi method khác ném NotSupported.
/// Thêm method vào IPlaceRepository thì phải thêm stub vào đây.
/// </summary>
internal sealed class StubPlaceRepository : IPlaceRepository
{
    public IReadOnlyList<MetroClusterPlaceReadModel> ClusterPlaces { get; init; } = [];

    public IReadOnlyDictionary<Guid, IReadOnlyList<Guid>> PlaceTagIds { get; init; } =
        new Dictionary<Guid, IReadOnlyList<Guid>>();

    public double? LastRadius { get; private set; }

    public Task<IReadOnlyList<MetroClusterPlaceReadModel>> GetMetroClusterPlacesAsync(double radiusMeters,
        CancellationToken cancellationToken = default)
    {
        LastRadius = radiusMeters;
        return Task.FromResult(ClusterPlaces);
    }

    public Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetPlaceTagIdsByPlaceIdsAsync(
        IReadOnlyList<Guid> placeIds, CancellationToken cancellationToken = default) =>
        Task.FromResult(PlaceTagIds);

    public Task<Point?> GetLocationAsync(Guid placeId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public Task<IReadOnlyList<PlaceSummaryResponse>> GetActiveWithinRadiusAsync(double latitude,
        double longitude, double radiusMeters, PlaceCategory? category,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<IReadOnlyList<AdminPlaceResponse>> GetAllForAdminAsync(
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<PagedResult<AdminPlaceResponse>> GetPagedForAdminAsync(AdminPlaceQuery query,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

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

internal sealed class StubMetroTimetableSource(MetroTimetable? timetable = null) : IMetroTimetableSource
{
    public MetroTimetable Timetable { get; } = timetable ?? MetroTimetableTestData.Valid();
}

internal sealed class StubStationRepository(IReadOnlyList<MetroStationSummaryResponse> stations)
    : IMetroStationRepository
{
    public Task<IReadOnlyList<MetroStationSummaryResponse>> GetAllAsync(
        CancellationToken cancellationToken = default) => Task.FromResult(stations);

    public Task<NearestStationResult?> FindNearestAsync(double latitude, double longitude,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public Task<NearestStationResult?> GetDistanceToStationAsync(Guid stationId, double latitude,
        double longitude, CancellationToken cancellationToken = default) => throw new NotSupportedException();
}
