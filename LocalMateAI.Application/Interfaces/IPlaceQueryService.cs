using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Interfaces;

public interface IPlaceQueryService
{
    Task<NearbyPlacesResponse> GetPlacesNearUserAsync(
        double latitude,
        double longitude,
        PlaceCategory? category,
        CancellationToken cancellationToken = default);
}
