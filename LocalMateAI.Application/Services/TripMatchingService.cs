using FluentValidation;
using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Application.Services;

public sealed class TripMatchingService(
    IValidator<TripRequestDto> validator,
    ITripCriteriaNormalizationService tripCriteriaNormalizationService,
    ITripOriginResolverService tripOriginResolverService,
    IMetroClusterMatchingService metroClusterMatchingService,
    ICandidateFilterService candidateFilterService,
    ITagSimilarityScorer tagSimilarityScorer,
    IPlaceRepository placeRepository,
    ISystemSettingProvider settings) : ITripMatchingService
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

        var anchorStation = new StationRefDto(origin.AnchorStation.Order, origin.AnchorStation.Name);

        if (!origin.IsWithinServiceArea)
        {
            return new TripMatchingResult(
                TripMatchingResultStatus.Success,
                Response: new TripMatchingResponse(
                    false,
                    origin.ServiceAreaFailure,
                    origin.NearestStation.StationName,
                    0,
                    criteria.BudgetTier.ToString(),
                    [],
                    [],
                    anchorStation),
                Origin: origin);
        }

        // BE-31: lọc địa điểm theo cụm ga Metro (PostGIS), quanh ga cột mốc (ga muốn chơi, hoặc ga lên).
        var candidates = await metroClusterMatchingService.GetCandidatesAsync(
            origin.AnchorStation.Id,
            cancellationToken);

        // BE-32: lọc ứng viên theo ngân sách
        var filtered = candidateFilterService.Filter(candidates, criteria);

        // BE-33: chấm điểm tương đồng sở thích ↔ tag địa điểm
        var placeTagIds = await placeRepository.GetPlaceTagIdsByPlaceIdsAsync(
            filtered.Passed.Select(candidate => candidate.PlaceId).ToList(),
            cancellationToken);

        var scored = tagSimilarityScorer.Score(filtered.Passed, placeTagIds, request.TagIds);

        // Xếp hạng: MatchScore giảm dần, gần ga cột mốc trước, PlaceId để thứ tự luôn ổn định.
        var ranked = scored
            .OrderByDescending(place => place.MatchScore)
            .ThenBy(place => CandidateRanking.DistanceToAnchorKm(place.Candidate, origin.AnchorStation))
            .ThenBy(place => place.Candidate.PlaceId)
            .ToList();

        // ItineraryScheduler là nơi duy nhất quyết định số chặng: chọn theo thứ hạng tới khi hết thời lượng hoặc ngân sách.
        var planning = await TripPlanningSettings.LoadAsync(settings, cancellationToken);
        var slots = ItineraryScheduler.Schedule(
            ranked.Select(place => ItineraryScheduler.ToScheduleInput(place.Candidate, planning)).ToList(),
            request.StartTime ?? ItineraryScheduler.DefaultStartTime,
            request.DurationHours,
            request.TravelMode,
            request.BudgetMax,
            origin.ToScheduleOrigin(),
            planning);

        var selected = slots
            .Select(slot => slot.SourceIndex)
            .Order()
            .Select(index => ranked[index])
            .ToList();

        var isSufficient = selected.Count >= 1;

        // Không có ứng viên nào trong ngân sách = thiếu địa điểm; có ứng viên mà không chặng nào vừa = thiếu giờ.
        var insufficiencyReason = isSufficient
            ? null
            : filtered.Passed.Count == 0
                ? TripInsufficiencyReasons.InsufficientCandidates
                : TripInsufficiencyReasons.DurationTooShort;

        return new TripMatchingResult(
            TripMatchingResultStatus.Success,
            Response: new TripMatchingResponse(
                isSufficient,
                insufficiencyReason,
                origin.NearestStation.StationName,
                selected.Count,
                criteria.BudgetTier.ToString(),
                selected,
                filtered.Excluded,
                anchorStation),
            Origin: origin);
    }
}