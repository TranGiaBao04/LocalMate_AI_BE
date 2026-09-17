using FluentValidation;
using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class TripMatchingService(
    IValidator<TripRequestDto> validator,
    ITripCriteriaNormalizationService tripCriteriaNormalizationService,
    ITripOriginResolverService tripOriginResolverService,
    IMetroClusterMatchingService metroClusterMatchingService,
    ICandidateFilterService candidateFilterService,
    ITagSimilarityScorer tagSimilarityScorer,
    IPlaceRepository placeRepository) : ITripMatchingService
{
    private const double CandidateSearchRadiusMeters = 800;

    public async Task<TripMatchingResult> MatchAsync(
        TripRequestDto request,
        CancellationToken cancellationToken = default)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return new TripMatchingResult(
                TripMatchingResultStatus.ValidationFailed,
                ValidationErrors: validationResult.Errors
                    .GroupBy(error => error.PropertyName)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.ErrorMessage).ToArray()));
        }

        var criteria = tripCriteriaNormalizationService.Normalize(request);

        var origin = await tripOriginResolverService.ResolveAsync(request, cancellationToken)
            ?? throw new InvalidOperationException("No metro stations found.");

        if (!origin.IsWithinServiceArea)
        {
            return new TripMatchingResult(
                TripMatchingResultStatus.Success,
                Response: new TripMatchingResponse(
                    false,
                    "OutOfServiceArea",
                    origin.NearestStation.StationName,
                    criteria.EstimatedStopCount,
                    criteria.BudgetTier.ToString(),
                    [],
                    []));
        }

        // BE-31: lọc địa điểm theo cụm ga Metro (PostGIS)
        var candidates = await metroClusterMatchingService.GetCandidatesAsync(
            origin.NearestStation.StationId,
            CandidateSearchRadiusMeters,
            cancellationToken);

        // BE-32: lọc ứng viên theo ngân sách & số chặng
        var filtered = candidateFilterService.Filter(candidates, criteria);

        // BE-33: chấm điểm tương đồng sở thích ↔ tag địa điểm
        var placeTagIds = await placeRepository.GetPlaceTagIdsByPlaceIdsAsync(
            filtered.Passed.Select(candidate => candidate.PlaceId).ToList(),
            cancellationToken);

        var scored = tagSimilarityScorer.Score(filtered.Passed, placeTagIds, request.TagIds);

        // Xếp hạng: MatchScore giảm dần, khoảng cách tăng dần
        var ranked = scored
            .OrderByDescending(place => place.MatchScore)
            .ThenBy(place => place.Candidate.DistanceFromStationMeters)
            .Take(criteria.EstimatedStopCount)
            .ToList();

        var isSufficient = ranked.Count >= criteria.EstimatedStopCount;

        return new TripMatchingResult(
            TripMatchingResultStatus.Success,
            Response: new TripMatchingResponse(
                isSufficient,
                isSufficient ? null : "InsufficientCandidates",
                origin.NearestStation.StationName,
                criteria.EstimatedStopCount,
                criteria.BudgetTier.ToString(),
                ranked,
                filtered.Excluded));
    }
}