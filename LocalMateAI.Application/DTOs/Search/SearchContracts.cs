using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.DTOs.Search;

public sealed record SearchQuery
{
    public const int MinKeywordLength = 2;
    public const int MaxKeywordLength = 100;
    public const int DefaultLimit = 5;
    public const int MaxLimit = 10;

    public string? Q { get; init; }

    // Số kết quả tối đa của TỪNG nhóm (ga, địa điểm, lịch trình mẫu).
    public int Limit { get; init; } = DefaultLimit;

    // Từ khoá đã cắt khoảng trắng hai đầu và gộp khoảng trắng liền nhau; không có chữ nào ⇒ null.
    public string? Keyword => string.IsNullOrWhiteSpace(Q)
        ? null
        : string.Join(' ', Q.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}

public enum SearchMatchField
{
    Name,
    Tag,
    Address,
    Semantic
}

public sealed record SearchStationItem(Guid Id, int Order, string Name, int PlaceCount);

public sealed record SearchPlaceItem(
    Guid Id,
    string Name,
    string Address,
    string Category,
    string? ImageUrl,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax,
    StationRefDto? Station,
    SearchMatchField MatchedOn);

public sealed record SearchCuratedItineraryItem(
    Guid Id,
    string Title,
    string? CoverImageUrl,
    int EstimatedDurationMinutes,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax,
    string? StationName);

public sealed record SearchGroup<T>(IReadOnlyList<T> Items, bool HasMore);

public sealed record SearchResponse(
    string Query,
    SearchGroup<SearchStationItem> Stations,
    SearchGroup<SearchPlaceItem> Places,
    SearchGroup<SearchCuratedItineraryItem> CuratedItineraries);

public enum SearchResultStatus
{
    Success,
    InvalidQuery
}

public sealed record SearchResult(
    SearchResultStatus Status,
    SearchResponse? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);
