using FluentValidation;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class TripFeasibilityService(
    IValidator<TripRequestDto> validator,
    ITripOriginResolverService tripOriginResolverService,
    ITripCriteriaNormalizationService tripCriteriaNormalizationService,
    IMetroClusterMatchingService metroClusterMatchingService,
    ICandidateFilterService candidateFilterService) : ITripFeasibilityService
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

        if (!origin.IsWithinServiceArea)
        {
            return TripFeasibilityResult.Succeeded(new TripFeasibilityResponse(
                false,
                "OutOfServiceArea",
                origin.NearestStation,
                criteria.DurationCategory.ToString(),
                criteria.BudgetTier.ToString(),
                0,
                0));
        }

        // Cùng nguồn ứng viên và cùng luật lọc ngân sách với /match và /generate.
        var candidates = await metroClusterMatchingService.GetCandidatesAsync(
            origin.NearestStation.StationId,
            MetroClusterMatchingService.CandidateRadiusMeters,
            cancellationToken);

        var filtered = candidateFilterService.Filter(candidates, criteria);

        // Chưa chấm điểm theo tag nên xếp theo khoảng cách tới ga: đúng thứ tự của generate khi user không chọn tag
        // (mọi điểm 0,5, hoà thì gần ga trước). Có chọn tag thì số chặng chỉ gần đúng.
        var planned = ItineraryScheduler.Schedule(
            filtered.Passed
                .OrderBy(candidate => candidate.DistanceFromStationMeters)
                .Select(ItineraryScheduler.ToScheduleInput)
                .ToList(),
            request.StartTime ?? ItineraryScheduler.DefaultStartTime,
            request.DurationHours,
            request.TravelMode,
            request.BudgetMax,
            new ScheduleOrigin(request.StartLatitude, request.StartLongitude));

        var isFeasible = planned.Count >= 1;

        return TripFeasibilityResult.Succeeded(new TripFeasibilityResponse(
            isFeasible,
            isFeasible ? null : "InsufficientCandidates",
            origin.NearestStation,
            criteria.DurationCategory.ToString(),
            criteria.BudgetTier.ToString(),
            planned.Count,
            candidates.Count));
    }

    private static IReadOnlyDictionary<string, string[]> ToValidationErrors(
        FluentValidation.Results.ValidationResult validationResult) =>
        validationResult.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());
}
