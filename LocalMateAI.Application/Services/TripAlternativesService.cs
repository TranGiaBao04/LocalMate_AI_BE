using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class TripAlternativesService(
    IUserRepository userRepository,
    IItineraryItemRepository itineraryItemRepository,
    IGeoService geoService,
    IMetroClusterMatchingService metroClusterMatchingService,
    IPlaceRepository placeRepository,
    IAlternativePlaceFinder alternativePlaceFinder) : ITripAlternativesService
{
    public const int DefaultLimit = 5;
    public const int MaxLimit = 10;
    private const double CandidateSearchRadiusMeters = 800;

    public async Task<TripAlternativesResult> GetAlternativesAsync(
        Guid userId,
        Guid tripId,
        Guid itemId,
        int? limit,
        CancellationToken cancellationToken = default)
    {
        if (tripId == Guid.Empty || itemId == Guid.Empty)
        {
            return TripAlternativesResult.InvalidIds();
        }

        var effectiveLimit = limit ?? DefaultLimit;
        if (effectiveLimit is < 1 or > MaxLimit)
        {
            return TripAlternativesResult.InvalidLimit();
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return TripAlternativesResult.MissingUser();
        }

        var item = await itineraryItemRepository.GetOwnedItemForAlternativesAsync(
            tripId,
            itemId,
            userId,
            cancellationToken);
        if (item is null)
        {
            return TripAlternativesResult.MissingItem();
        }

        if (item.TripStatus == TripStatus.Finalized)
        {
            return TripAlternativesResult.AlreadyFinalized();
        }

        var station = await geoService.FindNearestStationForPlaceAsync(item.PlaceId, cancellationToken)
            ?? throw new InvalidOperationException("No metro stations found.");

        var candidates = await metroClusterMatchingService.GetCandidatesAsync(
            station.StationId,
            CandidateSearchRadiusMeters,
            cancellationToken);

        // Địa điểm hiện tại nằm ngoài bán kính cụm ga thì không xác định được cụm để so sánh.
        var current = candidates.FirstOrDefault(candidate => candidate.PlaceId == item.PlaceId);
        if (current is null)
        {
            return TripAlternativesResult.Succeeded([]);
        }

        var placeTagIds = await placeRepository.GetPlaceTagIdsByPlaceIdsAsync(
            candidates.Select(candidate => candidate.PlaceId).ToList(),
            cancellationToken);

        var alternatives = alternativePlaceFinder
            .Find(current, candidates, placeTagIds, item.TripPlaceIds, effectiveLimit)
            .Select(ToResponse)
            .ToList();

        return TripAlternativesResult.Succeeded(alternatives);
    }

    private static AlternativePlaceResponse ToResponse(ScoredPlaceDto scored)
    {
        var candidate = scored.Candidate;
        return new AlternativePlaceResponse(
            candidate.PlaceId,
            candidate.PlaceName,
            candidate.Address,
            candidate.Latitude,
            candidate.Longitude,
            candidate.Category,
            candidate.EstimatedCostMin,
            candidate.EstimatedCostMax,
            candidate.ImageUrl,
            candidate.StationName,
            candidate.DistanceFromStationMeters,
            scored.MatchScore);
    }
}
