using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Domain.Enums;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IPlaceRepository
{
    Task<Point?> GetLocationAsync(
        Guid placeId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlaceSummaryResponse>> GetActiveWithinRadiusAsync(
        double latitude,
        double longitude,
        double radiusMeters,
        PlaceCategory? category,
        CancellationToken cancellationToken = default);
}
