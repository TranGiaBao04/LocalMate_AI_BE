using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class PlaceDistanceValidationService(
    IMetroStationRepository stationRepository,
    IGeoService geoService,
    IPlaceRepository placeRepository,
    ICoordinatesValidationService coordinatesValidationService) : IPlaceDistanceValidationService
{
    public const double MaxAllowedDistanceMeters = 1500.0;

    public async Task<PlaceDistanceValidationResult?> ValidateDistanceAsync(
        ValidatePlaceDistanceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!coordinatesValidationService.IsValidHcmcCoordinate(request.Latitude, request.Longitude))
        {
            return null;
        }

        NearestStationResult? stationResult;

        if (request.StationId.HasValue)
        {
            stationResult = await stationRepository.GetDistanceToStationAsync(
                request.StationId.Value,
                request.Latitude,
                request.Longitude,
                cancellationToken);
        }
        else
        {
            stationResult = await geoService.FindNearestStationAsync(
                request.Latitude,
                request.Longitude,
                cancellationToken);
        }

        if (stationResult is null)
        {
            return null;
        }

        var isWithinThreshold = stationResult.DistanceMeters <= MaxAllowedDistanceMeters;
        var hasWarning = !isWithinThreshold;
        var distanceKm = stationResult.DistanceMeters / 1000.0;

        string? warningMessage = hasWarning
            ? $"Khoảng cách từ địa điểm đến ga {stationResult.StationName} là {distanceKm:F2} km, vượt quá bán kính phục vụ khuyến nghị 1.5 km."
            : null;

        return new PlaceDistanceValidationResult(
            stationResult.StationId,
            stationResult.StationName,
            stationResult.StationLatitude,
            stationResult.StationLongitude,
            stationResult.DistanceMeters,
            isWithinThreshold,
            hasWarning,
            warningMessage);
    }

    public async Task<PlaceDistanceValidationResult?> ValidatePlaceDistanceAsync(
        Guid placeId,
        Guid? stationId = null,
        CancellationToken cancellationToken = default)
    {
        var location = await placeRepository.GetLocationAsync(placeId, cancellationToken);
        if (location is null)
        {
            return null;
        }

        var request = new ValidatePlaceDistanceRequest(location.Y, location.X, stationId);
        return await ValidateDistanceAsync(request, cancellationToken);
    }
}
