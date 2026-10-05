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
    ISystemSettingProvider settings,
    ISemanticPlaceScorer semanticScorer) : ITripMatchingService
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

        // Ghi chú chỉ đổi thứ hạng: không loại địa điểm nào và không vượt qua bước lọc ngân sách ở trên.
        var note = await ScoreNoteAsync(request.Note, scored, cancellationToken);
        var noteApplied = note.Scores.Count > 0;

        // Xếp hạng: điểm tag (và điểm ghi chú nếu áp dụng) giảm dần, gần ga cột mốc trước,
        // PlaceId để thứ tự luôn ổn định.
        var ranked = scored
            .Select(place => note.HighlightedPlaceIds.Contains(place.Candidate.PlaceId)
                ? place with { MatchesNote = true }
                : place)
            .OrderByDescending(place => noteApplied
                ? place.MatchScore * (1 - note.Weight)
                  + note.Scores.GetValueOrDefault(place.Candidate.PlaceId) * note.Weight
                : place.MatchScore)
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
                anchorStation,
                noteApplied),
            Origin: origin);
    }

    private async Task<NoteRanking> ScoreNoteAsync(
        string? rawNote,
        IReadOnlyList<ScoredPlaceDto> scored,
        CancellationToken cancellationToken)
    {
        var note = TripNoteRules.Normalize(rawNote);

        if (note is null || scored.Count == 0)
        {
            return NoteRanking.None;
        }

        var weightPercent = await settings.GetIntAsync(SystemSettingKeys.NoteWeightPercent, cancellationToken);
        if (weightPercent == 0)
        {
            return NoteRanking.None;
        }

        var similarities = await semanticScorer.ScoreAsync(
            note, SemanticPlaceScorer.PlanningTimeout, cancellationToken);
        if (similarities is null)
        {
            return NoteRanking.None;
        }

        var minSimilarity = await settings.GetIntAsync(SystemSettingKeys.SemanticMinSimilarityPercent, cancellationToken);
        var scores = TripNoteRules.Score(
            scored.Select(place => place.Candidate.PlaceId), similarities, minSimilarity);

        if (scores.Count == 0)
        {
            return NoteRanking.None;
        }

        // Câu lý do "hợp với ghi chú" chỉ dành cho những ứng viên khớp rõ: đạt sàn và sát ứng viên khớp nhất.
        var maxGap = await settings.GetIntAsync(SystemSettingKeys.SemanticMaxGapFromTopPercent, cancellationToken);
        var candidateSimilarities = scored
            .Select(place => place.Candidate.PlaceId)
            .Where(similarities.ContainsKey)
            .ToDictionary(placeId => placeId, placeId => similarities[placeId]);
        var highlighted = SemanticMatchRules.Select(candidateSimilarities, minSimilarity, maxGap)
            .Select(match => match.PlaceId)
            .ToHashSet();

        return new NoteRanking(scores, weightPercent / 100d, highlighted);
    }

    private sealed record NoteRanking(
        IReadOnlyDictionary<Guid, double> Scores,
        double Weight,
        IReadOnlySet<Guid> HighlightedPlaceIds)
    {
        public static readonly NoteRanking None = new(new Dictionary<Guid, double>(), 0d, new HashSet<Guid>());
    }
}