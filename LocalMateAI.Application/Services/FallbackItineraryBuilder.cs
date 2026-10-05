using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Application.Services;

/// <summary>
/// Xây dựng danh sách chặng dừng (itinerary stops) từ danh sách ứng viên đã xếp hạng.
/// Logic thuần — dễ unit test, không phụ thuộc DB/HTTP. Việc xếp giờ giao cho <see cref="ItineraryScheduler"/>.
/// </summary>
public static class FallbackItineraryBuilder
{
    public static IReadOnlyList<FallbackStopDto> BuildStops(
        IReadOnlyList<ScoredPlaceDto> rankedCandidates,
        int durationHours,
        decimal budgetMax,
        TravelMode travelMode = TravelMode.Auto,
        TimeOnly? startTime = null,
        ScheduleOrigin? origin = null,
        TripPlanningSettings? settings = null)
    {
        var inputs = rankedCandidates
            .Select(scored => ItineraryScheduler.ToScheduleInput(scored.Candidate, settings))
            .ToList();

        return ItineraryScheduler
            .Schedule(inputs, startTime ?? ItineraryScheduler.DefaultStartTime, durationHours, travelMode, budgetMax,
                origin, settings)
            .Select((slot, index) =>
            {
                var scored = rankedCandidates[slot.SourceIndex];
                var candidate = scored.Candidate;

                return new FallbackStopDto(
                    candidate.PlaceId,
                    candidate.PlaceName,
                    candidate.Address,
                    candidate.Latitude,
                    candidate.Longitude,
                    candidate.Category,
                    OrderIndex: index,
                    ScheduledTime: slot.ScheduledTime,
                    EstimatedDurationMinutes: slot.DurationMinutes,
                    EstimatedBudget: candidate.EstimatedCostMax,
                    Reasoning: BuildReasoning(scored),
                    MatchScore: scored.MatchScore,
                    DistanceFromStationMeters: candidate.DistanceFromStationMeters,
                    StationName: candidate.StationName);
            })
            .ToList();
    }

    public const int NoteReasonDescriptionLength = 120;

    internal static string BuildReasoning(ScoredPlaceDto scored)
    {
        var candidate = scored.Candidate;
        var distance = $"cách ga {candidate.StationName} {candidate.DistanceFromStationMeters:0} m";

        if (scored.MatchesNote)
        {
            var description = Shorten(candidate.Description, NoteReasonDescriptionLength);
            return description is null
                ? $"Hợp với ghi chú của bạn, {distance}"
                : $"Hợp với ghi chú của bạn: {description}. Cách ga {candidate.StationName} {candidate.DistanceFromStationMeters:0} m";
        }

        // Không có tag trùng (kể cả khi user không chọn tag, điểm trung tính 0.5) thì không nhắc tới sở thích.
        return scored.MatchedTagIds.Count > 0
            ? $"Khớp sở thích của bạn {scored.MatchScore:P0}, {distance}"
            : $"Cách ga {candidate.StationName} {candidate.DistanceFromStationMeters:0} m";
    }

    // Cắt ở ranh giới từ và thêm dấu ba chấm; bỏ dấu chấm cuối để không thành hai dấu chấm liền nhau.
    private static string? Shorten(string? text, int maxLength)
    {
        var trimmed = text?.Trim().TrimEnd('.').TrimEnd();
        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        var cut = trimmed[..maxLength];
        var lastSpace = cut.LastIndexOf(' ');
        return (lastSpace > 0 ? cut[..lastSpace] : cut).TrimEnd(',', ';', ':') + "…";
    }
}
