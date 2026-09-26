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
                    0,
                    criteria.BudgetTier.ToString(),
                    [],
                    []));
        }

        // BE-31: lọc địa điểm theo cụm ga Metro (PostGIS)
        var candidates = await metroClusterMatchingService.GetCandidatesAsync(
            origin.NearestStation.StationId,
            MetroClusterMatchingService.CandidateRadiusMeters,
            cancellationToken);

        // BE-32: lọc ứng viên theo ngân sách
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
            .ToList();

        // ItineraryScheduler là nơi duy nhất quyết định số chặng: chọn theo thứ hạng tới khi hết thời lượng hoặc ngân sách.
        var slots = ItineraryScheduler.Schedule(
            ranked.Select(place => ItineraryScheduler.ToScheduleInput(place.Candidate)).ToList(),
            request.StartTime ?? ItineraryScheduler.DefaultStartTime,
            request.DurationHours,
            request.TravelMode,
            request.BudgetMax,
            new ScheduleOrigin(request.StartLatitude, request.StartLongitude));

        var selected = slots
            .Select(slot => slot.SourceIndex)
            .Order()
            .Select(index => ranked[index])
            .ToList();

        var isSufficient = selected.Count >= 1;

        return new TripMatchingResult(
            TripMatchingResultStatus.Success,
            Response: new TripMatchingResponse(
                isSufficient,
                isSufficient ? null : "InsufficientCandidates",
                origin.NearestStation.StationName,
                selected.Count,
                criteria.BudgetTier.ToString(),
                selected,
                filtered.Excluded));
    }
}