using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Application.Services;

public sealed class StationSuggestionService(
    IPlaceRepository placeRepository,
    IMetroStationRepository stationRepository,
    ISystemSettingProvider settings) : IStationSuggestionService
{
    public const int MaxSuggestions = 3;

    public async Task<IReadOnlyList<SuggestedStationDto>> SuggestAsync(
        TripOriginResolution origin,
        CancellationToken cancellationToken = default)
    {
        var minPlaceCount = await settings.GetIntAsync(SystemSettingKeys.MinActivePlacesPerStation, cancellationToken);
        var radiusMeters = await settings.GetIntAsync(SystemSettingKeys.StationClusterRadiusMeters, cancellationToken);
        var adjacentStationWindow = await settings.GetIntAsync(SystemSettingKeys.AdjacentStationWindow, cancellationToken);

        var stations = await stationRepository.GetAllAsync(cancellationToken);

        // Cùng nguồn và cùng cách gán địa điểm vào ga với MetroClusterMatchingService, nên số đếm ở đây
        // đúng bằng số ứng viên (chưa lọc ngân sách) mà người dùng sẽ có nếu chọn ga đó.
        var placeCountByStationOrder = (await placeRepository.GetMetroClusterPlacesAsync(radiusMeters, cancellationToken))
            .GroupBy(row => row.StationOrder)
            .ToDictionary(group => group.Key, group => group.Count());

        return stations
            .Where(station => station.Id != origin.AnchorStation.Id)
            .Select(station => new
            {
                Station = station,
                PlaceCount = Enumerable
                    .Range(station.Order - adjacentStationWindow, adjacentStationWindow * 2 + 1)
                    .Sum(order => placeCountByStationOrder.GetValueOrDefault(order))
            })
            .Where(entry => entry.PlaceCount >= minPlaceCount)
            .OrderBy(entry => TravelTimeEstimator.HaversineKm(
                origin.StartLatitude, origin.StartLongitude, entry.Station.Latitude, entry.Station.Longitude))
            .ThenBy(entry => entry.Station.Order)
            .Take(MaxSuggestions)
            .Select(entry => new SuggestedStationDto(entry.Station.Order, entry.Station.Name, entry.PlaceCount))
            .ToList();
    }
}
