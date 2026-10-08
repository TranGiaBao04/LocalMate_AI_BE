using LocalMateAI.Application.DTOs.Places;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IPlaceDistanceValidationService
{
    Task<PlaceDistanceValidationResult> ValidateDistanceAsync(
        ValidatePlaceDistanceRequest request,
        CancellationToken cancellationToken = default);

    Task<PlaceDistanceValidationResult> ValidatePlaceDistanceAsync(
        Guid placeId,
        Guid? stationId = null,
        CancellationToken cancellationToken = default);
}
