using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Application.Services;

public sealed class PlaceDistanceValidationService(
    IMetroStationRepository stationRepository,
    IGeoService geoService,
    IPlaceRepository placeRepository,
    ICoordinatesValidationService coordinatesValidationService,
    ISystemSettingProvider settingProvider) : IPlaceDistanceValidationService
{
    public const double DefaultMaxAllowedDistanceMeters = 1500.0;

    public async Task<PlaceDistanceValidationResult> ValidateDistanceAsync(
        ValidatePlaceDistanceRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!coordinatesValidationService.IsValidHcmcCoordinate(request.Latitude, request.Longitude))
        {
            return CreateEmptyResult(PlaceDistanceValidationStatus.InvalidCoordinates);
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
            return CreateEmptyResult(PlaceDistanceValidationStatus.StationNotFound);
        }

        var configuredRadius = await settingProvider.GetIntAsync(
            SystemSettingKeys.StationClusterRadiusMeters, cancellationToken);

        var thresholdMeters = configuredRadius > 0 ? (double)configuredRadius : DefaultMaxAllowedDistanceMeters;

        var isWithinThreshold = stationResult.DistanceMeters <= thresholdMeters;
        var hasWarning = !isWithinThreshold;
        var distanceKm = stationResult.DistanceMeters / 1000.0;
        var thresholdKm = thresholdMeters / 1000.0;

        string? warningMessage = hasWarning
            ? $"Khoảng cách từ địa điểm đến ga {stationResult.StationName} là {distanceKm:F2} km, vượt quá bán kính phục vụ khuyến nghị {thresholdKm:F1} km."
            : null;

        return new PlaceDistanceValidationResult(
            stationResult.StationId,
            stationResult.StationName,
            stationResult.StationLatitude,
            stationResult.StationLongitude,
            stationResult.DistanceMeters,
            isWithinThreshold,
            hasWarning,
            warningMessage,
            PlaceDistanceValidationStatus.Success);
    }

    public async Task<PlaceDistanceValidationResult> ValidatePlaceDistanceAsync(
        Guid placeId,
        Guid? stationId = null,
        CancellationToken cancellationToken = default)
    {
        var location = await placeRepository.GetLocationAsync(placeId, cancellationToken);
        if (location is null)
        {
            return CreateEmptyResult(PlaceDistanceValidationStatus.PlaceNotFound);
        }

        var request = new ValidatePlaceDistanceRequest(location.Y, location.X, stationId);
        return await ValidateDistanceAsync(request, cancellationToken);
    }

    private static PlaceDistanceValidationResult CreateEmptyResult(PlaceDistanceValidationStatus status) =>
        new(
            Guid.Empty,
            string.Empty,
            0,
            0,
            0,
            false,
            false,
            null,
            status);
}
