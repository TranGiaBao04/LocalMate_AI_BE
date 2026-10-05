using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Application.Services;

/// <summary>
/// Heuristic Fallback Engine (BE-38): khi luồng AI (LLM) sinh lịch trình bị
/// timeout/lỗi, engine này chạy lại pipeline heuristic (BE-31 → BE-32 → BE-33)
/// và dựng draft itinerary xác định, giải thích được — không tốn quota LLM.
/// </summary>
public sealed class HeuristicFallbackEngine(
    ITripMatchingService tripMatchingService,
    ISystemSettingProvider settings) : IHeuristicFallbackEngine
{
    public async Task<FallbackItineraryResult> GenerateFallbackAsync(
        TripRequestDto request,
        string fallbackReason,
        CancellationToken cancellationToken = default)
    {
        var match = await tripMatchingService.MatchAsync(request, cancellationToken);

        if (match.Status == TripMatchingResultStatus.ValidationFailed)
        {
            return new FallbackItineraryResult(
                FallbackItineraryStatus.ValidationFailed,
                ValidationErrors: match.ValidationErrors);
        }

        var matched = match.Response!;

        if (!matched.IsSufficient)
        {
            return new FallbackItineraryResult(
                FallbackItineraryStatus.Success,
                Payload: new FallbackItineraryPayload(
                    fallbackReason,
                    IsSufficient: false,
                    matched.InsufficiencyReason,
                    matched.StationName,
                    matched.EstimatedStopCount,
                    matched.BudgetTier,
                    [],
                    matched.AnchorStation,
                    matched.NoteApplied),
                Origin: match.Origin);
        }

        var origin = match.Origin
            ?? throw new InvalidOperationException("Trip matching succeeded without a resolved origin.");

        // matched.Candidates đã được xếp hạng (score desc, distance asc) & cắt đúng stopCount.
        var planning = await TripPlanningSettings.LoadAsync(settings, cancellationToken);
        var stops = FallbackItineraryBuilder.BuildStops(
            matched.Candidates, request.DurationHours, request.BudgetMax, request.TravelMode,
            request.StartTime, origin.ToScheduleOrigin(), planning);

        return new FallbackItineraryResult(
            FallbackItineraryStatus.Success,
            Payload: new FallbackItineraryPayload(
                fallbackReason,
                IsSufficient: true,
                InsufficiencyReason: null,
                matched.StationName,
                matched.EstimatedStopCount,
                matched.BudgetTier,
                stops,
                matched.AnchorStation,
                matched.NoteApplied),
            Origin: origin);
    }
}