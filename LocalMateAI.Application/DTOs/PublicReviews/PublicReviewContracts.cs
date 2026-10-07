namespace LocalMateAI.Application.DTOs.PublicReviews;

public sealed record PublicReviewQuery
{
    public const int DefaultLimit = 3;
    public const int MaxLimit = 12;

    public int Limit { get; init; } = DefaultLimit;
}

public sealed record PublicReviewPlace(Guid Id, string Name);

// Thẻ đánh giá ở trang landing: không kèm id người dùng, email, id chuyến hay id chặng.
public sealed record PublicReviewItem(
    Guid Id,
    int Rating,
    string Comment,
    IReadOnlyList<string> QuickTags,
    DateTime CreatedAt,
    string ReviewerName,
    PublicReviewPlace Place);

public sealed record PublicReviewsResponse(IReadOnlyList<PublicReviewItem> Items, DateTime GeneratedAt);

public enum PublicReviewsResultStatus
{
    Success,
    InvalidQuery
}

public sealed record PublicReviewsResult(
    PublicReviewsResultStatus Status,
    PublicReviewsResponse? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);
