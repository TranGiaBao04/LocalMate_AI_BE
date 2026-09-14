using FluentValidation;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class TripFeasibilityService(
    IValidator<TripRequestDto> validator,
    ITripOriginResolverService tripOriginResolverService,
    ITripCriteriaNormalizationService tripCriteriaNormalizationService,
    IPlaceRepository placeRepository) : ITripFeasibilityService
{
    private const double CandidateSearchRadiusMeters = 800;

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
                criteria.EstimatedStopCount,
                0));
        }

        var candidatePlaces = await placeRepository.GetActiveWithinRadiusAsync(
            origin.NearestStation.StationLatitude,
            origin.NearestStation.StationLongitude,
            CandidateSearchRadiusMeters,
            category: null,
            cancellationToken);

        var isFeasible = candidatePlaces.Count >= criteria.EstimatedStopCount;

        return TripFeasibilityResult.Succeeded(new TripFeasibilityResponse(
            isFeasible,
            isFeasible ? null : "InsufficientCandidates",
            origin.NearestStation,
            criteria.DurationCategory.ToString(),
            criteria.BudgetTier.ToString(),
            criteria.EstimatedStopCount,
            candidatePlaces.Count));
    }

    private static IReadOnlyDictionary<string, string[]> ToValidationErrors(
        FluentValidation.Results.ValidationResult validationResult) =>
        validationResult.Errors
            .GroupBy(error => error.PropertyName)
            .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).ToArray());
}
