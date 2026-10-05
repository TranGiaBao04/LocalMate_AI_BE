using FluentValidation;
using LocalMateAI.Application.DTOs.Search;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Application.Services;

public sealed class SearchService(
    ISearchRepository repository,
    ISystemSettingProvider settings,
    IValidator<SearchQuery> validator,
    ISemanticPlaceScorer semanticScorer) : ISearchService
{
    // Từ khoá ngắn hơn mức này quá ít nghĩa để so theo nghĩa, và thường là người dùng đang gõ dở.
    public const int MinSemanticKeywordLength = 4;

    public async Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return new SearchResult(SearchResultStatus.InvalidQuery, ValidationErrors: validation.Errors
                .GroupBy(failure => failure.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray()));
        }

        var keyword = query.Keyword!;
        // Lấy dư một dòng mỗi nhóm để biết còn kết quả hay không mà không phải đếm tổng.
        var take = query.Limit + 1;
        var radiusMeters = await settings.GetIntAsync(SystemSettingKeys.StationClusterRadiusMeters, cancellationToken);

        // Chạy lần lượt vì ba truy vấn dùng chung một DbContext.
        var stations = await repository.SearchStationsAsync(keyword, take, radiusMeters, cancellationToken);
        var places = await repository.SearchPlacesAsync(keyword, take, radiusMeters, cancellationToken);
        var itineraries = await repository.SearchCuratedItinerariesAsync(keyword, take, cancellationToken);

        // So chuỗi trước, theo nghĩa sau: kết quả theo nghĩa chỉ lấp chỗ còn trống của nhóm địa điểm.
        if (places.Count < query.Limit && keyword.Length >= MinSemanticKeywordLength)
        {
            var semantic = await FindSemanticPlacesAsync(
                keyword, places, take - places.Count, radiusMeters, cancellationToken);
            places = [.. places, .. semantic];
        }

        return new SearchResult(SearchResultStatus.Success, new SearchResponse(
            keyword,
            ToGroup(stations, query.Limit),
            ToGroup(places, query.Limit),
            ToGroup(itineraries, query.Limit)));
    }

    private async Task<IReadOnlyList<SearchPlaceItem>> FindSemanticPlacesAsync(
        string keyword,
        IReadOnlyList<SearchPlaceItem> textMatches,
        int take,
        double radiusMeters,
        CancellationToken cancellationToken)
    {
        var scores = await semanticScorer.ScoreAsync(keyword, SemanticPlaceScorer.SearchTimeout, cancellationToken);
        if (scores is null)
        {
            return [];
        }

        var minSimilarity = await settings.GetIntAsync(SystemSettingKeys.SemanticMinSimilarityPercent, cancellationToken);
        var maxGap = await settings.GetIntAsync(SystemSettingKeys.SemanticMaxGapFromTopPercent, cancellationToken);
        var alreadyFound = textMatches.Select(place => place.Id).ToHashSet();

        var ranked = SemanticMatchRules.Select(scores, minSimilarity, maxGap)
            .Where(match => !alreadyFound.Contains(match.PlaceId))
            .ToList();

        if (ranked.Count == 0)
        {
            return [];
        }

        // Vector có thể còn của địa điểm vừa bị ẩn: repository chỉ trả địa điểm đang hiển thị.
        var visible = (await repository.GetSemanticPlacesAsync(
                ranked.Select(match => match.PlaceId).ToArray(), radiusMeters, cancellationToken))
            .ToDictionary(place => place.Id);

        return ranked
            .Where(match => visible.ContainsKey(match.PlaceId))
            .Select(match => visible[match.PlaceId])
            .Take(take)
            .ToList();
    }

    private static SearchGroup<T> ToGroup<T>(IReadOnlyList<T> rows, int limit) =>
        new(rows.Take(limit).ToList(), rows.Count > limit);
}
