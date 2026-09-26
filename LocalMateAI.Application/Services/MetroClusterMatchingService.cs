using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class MetroClusterMatchingService(
    IPlaceRepository placeRepository,
    IMetroStationRepository stationRepository) : IMetroClusterMatchingService
{
    /// <summary>Bán kính (mét) quanh ga để tìm địa điểm ứng viên; dùng chung cho matching và feasibility-check.</summary>
    public const double CandidateRadiusMeters = 800;

    private const int AdjacentStationWindow = 1; // ±1 ga dọc tuyến Metro số 1

    public async Task<IReadOnlyList<PlaceCandidateDto>> GetCandidatesAsync(
        Guid originStationId,
        double radiusMeters,
        CancellationToken cancellationToken = default)
    {
        var stations = await stationRepository.GetAllAsync(cancellationToken);
        var originStation = stations.FirstOrDefault(station => station.Id == originStationId);

        if (originStation is null)
        {
            return [];
        }

        var rows = await placeRepository.GetMetroClusterPlacesAsync(radiusMeters, cancellationToken);

        var minOrder = originStation.Order - AdjacentStationWindow;
        var maxOrder = originStation.Order + AdjacentStationWindow;

        return rows
            .Where(row => row.StationOrder >= minOrder && row.StationOrder <= maxOrder)
            .Select(row => new PlaceCandidateDto(
                row.PlaceId,
                row.PlaceName,
                row.PlaceAddress,
                row.PlaceLatitude,
                row.PlaceLongitude,
                row.PlaceCategory,
                row.EstimatedCostMin,
                row.EstimatedCostMax,
                row.ImageUrl,
                row.StationId,
                row.StationName,
                row.StationOrder,
                row.DistanceFromStationMeters))
            .OrderBy(candidate => candidate.StationOrder)
            .ThenBy(candidate => candidate.DistanceFromStationMeters)
            .ToList();
    }
}