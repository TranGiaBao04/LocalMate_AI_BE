using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using NetTopologySuite.Geometries;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IPlaceRepository
{
    Task<IReadOnlyList<MetroClusterPlaceReadModel>> GetMetroClusterPlacesAsync(
        double radiusMeters,
        CancellationToken cancellationToken = default);

    Task<Point?> GetLocationAsync(
        Guid placeId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlaceSummaryResponse>> GetActiveWithinRadiusAsync(
        double latitude,
        double longitude,
        double radiusMeters,
        PlaceCategory? category,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, IReadOnlyList<Guid>>> GetPlaceTagIdsByPlaceIdsAsync(
        IReadOnlyList<Guid> placeIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminPlaceResponse>> GetAllForAdminAsync(
        CancellationToken cancellationToken = default);

    Task<AdminPlaceResponse?> GetAdminByIdAsync(
        Guid placeId,
        CancellationToken cancellationToken = default);

    Task<Place?> GetByIdAsync(
        Guid placeId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        Place place,
        CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    Task<DeletePlacePersistenceResult> DeleteForAdminAsync(
        Guid placeId,
        CancellationToken cancellationToken = default);

    Task<PlaceReadModel?> GetActiveByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
