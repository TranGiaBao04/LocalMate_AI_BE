using LocalMateAI.Application.DTOs.Common;

namespace LocalMateAI.Application.DTOs.PlaceReviews;

public sealed record PlaceReviewQuery : PagedQuery
{
    public static readonly IReadOnlyCollection<string> SortFields = ["createdAt", "rating"];

    // Bỏ trống ⇒ lấy mọi mức sao.
    public int? Rating { get; init; }
}

// Đánh giá hiển thị công khai trên trang địa điểm: không kèm id người dùng, email hay id chặng.
public sealed record PublicPlaceReviewResponse(
    Guid Id,
    int Rating,
    IReadOnlyList<string> QuickTags,
    string? Comment,
    DateTime CreatedAt,
    string ReviewerName);

public sealed record PlaceReviewSummary(int ReviewCount, int RatingSum)
{
    // Tính bằng decimal để 4,35 làm tròn thành 4,4 (double sẽ ra 4,3). Chưa có đánh giá ⇒ null.
    public decimal? AverageRating => ReviewCount == 0
        ? null
        : Math.Round((decimal)RatingSum / ReviewCount, 1, MidpointRounding.AwayFromZero);
}

public enum PlaceReviewListResultStatus
{
    Success,
    InvalidQuery,
    PlaceNotFound
}

public sealed record PlaceReviewListResult(
    PlaceReviewListResultStatus Status,
    PagedResult<PublicPlaceReviewResponse>? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);
