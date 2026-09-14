using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IPlaceQueryService
{
    Task<IReadOnlyList<MetroExperienceClusterResponse>> GetMetroClustersAsync(
        CancellationToken cancellationToken = default);

    Task<NearbyPlacesResponse> GetPlacesNearUserAsync(
        double latitude,
        double longitude,
        PlaceCategory? category,
        CancellationToken cancellationToken = default);
}
