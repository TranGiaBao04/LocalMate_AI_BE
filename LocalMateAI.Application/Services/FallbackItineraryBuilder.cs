using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Domain.Enums;

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
        TravelMode travelMode = TravelMode.Auto,
        TimeOnly? startTime = null)
    {
        var inputs = rankedCandidates
            .Select(scored => new ScheduleInput(
                scored.Candidate.Latitude,
                scored.Candidate.Longitude,
                ItineraryScheduler.VisitMinutesFor(scored.Candidate.Category)))
            .ToList();

        return ItineraryScheduler
            .Schedule(inputs, startTime ?? ItineraryScheduler.DefaultStartTime, durationHours, travelMode)
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

    internal static string BuildReasoning(ScoredPlaceDto scored)
    {
        var candidate = scored.Candidate;
        return $"Fallback heuristic: khớp sở thích {scored.MatchScore:P0}, " +
               $"cách ga {candidate.StationName} {candidate.DistanceFromStationMeters:0}m";
    }
}
