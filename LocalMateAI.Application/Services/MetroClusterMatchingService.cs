using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Application.Services;

public sealed class MetroClusterMatchingService(
    IPlaceRepository placeRepository,
    IMetroStationRepository stationRepository,
    ISystemSettingProvider settings) : IMetroClusterMatchingService
{
    public async Task<IReadOnlyList<PlaceCandidateDto>> GetCandidatesAsync(
        Guid originStationId,
        CancellationToken cancellationToken = default)
    {
        var stations = await stationRepository.GetAllAsync(cancellationToken);
        var originStation = stations.FirstOrDefault(station => station.Id == originStationId);

        if (originStation is null)
        {
            return [];
        }

        // Bán kính cụm ga và số ga kề dọc tuyến Metro số 1 do admin chỉnh (mặc định 800 m, ±1 ga).
        var radiusMeters = await settings.GetIntAsync(SystemSettingKeys.StationClusterRadiusMeters, cancellationToken);
        var adjacentStationWindow = await settings.GetIntAsync(SystemSettingKeys.AdjacentStationWindow, cancellationToken);

        var rows = await placeRepository.GetMetroClusterPlacesAsync(radiusMeters, cancellationToken);

        var minOrder = originStation.Order - adjacentStationWindow;
        var maxOrder = originStation.Order + adjacentStationWindow;

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
                row.DistanceFromStationMeters,
                row.Description))
            .OrderBy(candidate => candidate.StationOrder)
            .ThenBy(candidate => candidate.DistanceFromStationMeters)
            .ToList();
    }
}
