using FluentValidation;
using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Application.Services;

public sealed class TripFeasibilityService(
    IValidator<TripRequestDto> validator,
    ITripOriginResolverService tripOriginResolverService,
    ITripCriteriaNormalizationService tripCriteriaNormalizationService,
    IMetroClusterMatchingService metroClusterMatchingService,
    ICandidateFilterService candidateFilterService,
    IStationSuggestionService stationSuggestionService,
    ISystemSettingProvider settings) : ITripFeasibilityService
{
    public async Task<TripFeasibilityResult> CheckFeasibilityAsync(
        TripRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return TripFeasibilityResult.ValidationFailed(ToValidationErrors(validationResult));
        }

        var criteria = tripCriteriaNormalizationService.Normalize(request);

        var origin = await tripOriginResolverService.ResolveAsync(request, cancellationToken)
            ?? throw new InvalidOperationException("No metro stations found.");

        var anchorStation = new StationRefDto(origin.AnchorStation.Order, origin.AnchorStation.Name);

        if (!origin.IsWithinServiceArea)
        {
            return TripFeasibilityResult.Succeeded(new TripFeasibilityResponse(
                false,
                origin.ServiceAreaFailure,
                origin.NearestStation,
                criteria.DurationCategory.ToString(),
                criteria.BudgetTier.ToString(),
                0,
                0,
                anchorStation,
                []));
        }

        // Cùng nguồn ứng viên và cùng luật lọc ngân sách với /match và /generate.
        var candidates = await metroClusterMatchingService.GetCandidatesAsync(
            origin.AnchorStation.Id,
            cancellationToken);

        var filtered = candidateFilterService.Filter(candidates, criteria);

        // Chưa chấm điểm theo tag nên xếp theo khoảng cách tới ga cột mốc: đúng thứ tự của generate khi user
        // không chọn tag (mọi điểm 0,5, hoà thì gần ga cột mốc trước). Có chọn tag thì số chặng chỉ gần đúng.
        var planning = await TripPlanningSettings.LoadAsync(settings, cancellationToken);
        var planned = ItineraryScheduler.Schedule(
            filtered.Passed
                .OrderBy(candidate => CandidateRanking.DistanceToAnchorKm(candidate, origin.AnchorStation))
                .ThenBy(candidate => candidate.PlaceId)
                .Select(candidate => ItineraryScheduler.ToScheduleInput(candidate, planning))
                .ToList(),
            request.StartTime ?? ItineraryScheduler.DefaultStartTime,
            request.DurationHours,
            request.TravelMode,
            request.BudgetMax,
            origin.ToScheduleOrigin(),
            planning);

        var isFeasible = planned.Count >= 1;

        // Không có ứng viên nào trong ngân sách = thiếu địa điểm; có ứng viên mà không chặng nào vừa = thiếu giờ.
        var reason = isFeasible
            ? null
            : filtered.Passed.Count == 0
                ? TripInsufficiencyReasons.InsufficientCandidates
                : TripInsufficiencyReasons.DurationTooShort;

        // Chỉ gợi ý ga khác khi vấn đề là thiếu địa điểm; thiếu giờ thì đổi ga không giúp gì.
        var suggestedStations = reason == TripInsufficiencyReasons.InsufficientCandidates
            ? await stationSuggestionService.SuggestAsync(origin, cancellationToken)
            : [];

        return TripFeasibilityResult.Succeeded(new TripFeasibilityResponse(
            isFeasible,
            reason,
            origin.NearestStation,
            criteria.DurationCategory.ToString(),
            criteria.BudgetTier.ToString(),
            planned.Count,
            candidates.Count,
            anchorStation,
            suggestedStations));
    }

    private static IReadOnlyDictionary<string, string[]> ToValidationErrors(
        FluentValidation.Results.ValidationResult validationResult) =>
        validationResult.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());
}
