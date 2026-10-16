using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class TripAlternativesService(
    IUserRepository userRepository,
    IItineraryItemRepository itineraryItemRepository,
    IGeoService geoService,
    IMetroClusterMatchingService metroClusterMatchingService,
    IPlaceRepository placeRepository,
    IAlternativePlaceFinder alternativePlaceFinder,
    ISystemSettingProvider settings) : ITripAlternativesService
{
    public const int DefaultLimit = 5;
    public const int MaxLimit = 10;

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

        var maxCostIncreasePercent = await settings.GetIntAsync(
            SystemSettingKeys.AlternativeMaxCostIncreasePercent,
            cancellationToken);

        // Lấy đủ danh sách đã xếp hạng, rồi mới đẩy nơi đang đóng xuống cuối và cắt limit,
        // để nơi đang mở ở hạng thấp vẫn được gợi ý.
        var ranked = alternativePlaceFinder.Find(
            current, candidates, placeTagIds, item.TripPlaceIds, candidates.Count, maxCostIncreasePercent);

        var alternatives = ranked
            .Select(scored => (Scored: scored, Status: OpeningStatusFor(scored.Candidate, item)))
            .OrderBy(entry => StatusRank(entry.Status)) // OrderBy giữ thứ tự cũ trong cùng nhóm
            .Take(effectiveLimit)
            .Select(entry => ToResponse(entry.Scored, entry.Status))
            .ToList();

        return TripAlternativesResult.Succeeded(alternatives);
    }

    // Theo khung giờ HIỆN TẠI của chặng; sau khi thay, giờ tới có thể lệch vài phút vì đường đi khác.
    private static PlaceOpeningStatus OpeningStatusFor(
        PlaceCandidateDto candidate,
        OwnedItemAlternativesReadModel item) =>
        item.TripPlannedStartAt is not { } plannedAt
            ? PlaceOpeningStatus.Unknown // trip cũ chưa đặt ngày
            : OpeningWindows.StatusFor(
                candidate.OpeningHours ?? [],
                DateOnly.FromDateTime(plannedAt),
                item.VisitStartMinute,
                item.VisitStartMinute + item.VisitDurationMinutes);

    private static int StatusRank(PlaceOpeningStatus status) => status switch
    {
        PlaceOpeningStatus.Open => 0,
        PlaceOpeningStatus.Unknown => 1,
        _ => 2
    };

    private static AlternativePlaceResponse ToResponse(ScoredPlaceDto scored, PlaceOpeningStatus status)
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
            scored.MatchScore,
            status);
    }
}
