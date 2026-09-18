using LocalMateAI.Application.DTOs.Matching;

namespace LocalMateAI.Application.Services;

/// <summary>
/// Xây dựng danh sách chặng dừng (itinerary stops) từ danh sách ứng viên đã xếp hạng.
/// Logic thuần — dễ unit test, không phụ thuộc DB/HTTP.
/// </summary>
public static class FallbackItineraryBuilder
{
    public const int StartHour = 8; // Xuất phát mặc định 08:00
    public const int MinutesPerStop = 90; // Khớp với AverageMinutesPerStop của TripCriteriaNormalizationService

    public static IReadOnlyList<FallbackStopDto> BuildStops(
        IReadOnlyList<ScoredPlaceDto> rankedCandidates)
    {
        var startTime = new TimeOnly(StartHour, 0);
        var stops = new List<FallbackStopDto>(rankedCandidates.Count);

        for (var index = 0; index < rankedCandidates.Count; index++)
        {
            var scored = rankedCandidates[index];
            var candidate = scored.Candidate;

            var scheduledTime = startTime.AddMinutes(index * MinutesPerStop);
            var budget = candidate.EstimatedCostMax;
            var reasoning = BuildReasoning(scored);

            stops.Add(new FallbackStopDto(
                candidate.PlaceId,
                candidate.PlaceName,
                candidate.Address,
                candidate.Latitude,
                candidate.Longitude,
                candidate.Category,
                OrderIndex: index,
                ScheduledTime: scheduledTime,
                EstimatedDurationMinutes: MinutesPerStop,
                EstimatedBudget: budget,
                Reasoning: reasoning,
                MatchScore: scored.MatchScore,
                DistanceFromStationMeters: candidate.DistanceFromStationMeters,
                StationName: candidate.StationName));
        }

        return stops;
    }

    internal static string BuildReasoning(ScoredPlaceDto scored)
    {
        var candidate = scored.Candidate;
        return $"Fallback heuristic: khớp sở thích {scored.MatchScore:P0}, " +
               $"cách ga {candidate.StationName} {candidate.DistanceFromStationMeters:0}m";
    }
}