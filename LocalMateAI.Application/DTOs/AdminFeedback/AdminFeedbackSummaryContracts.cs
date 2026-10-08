using System.Text.Json.Serialization;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.AdminFeedback;

// Số liệu thô của một khoảng ngày; mức sao / mã không có lượt nào thì không có trong từ điển.
public sealed record AdminFeedbackSummaryData(
    IReadOnlyDictionary<int, int> ReviewCountsByRating,
    IReadOnlyDictionary<string, int> ReviewQuickTagCounts,
    IReadOnlyDictionary<FeedbackQuickTag, int> TripFeedbackQuickTagCounts,
    IReadOnlyList<LowRatedPlaceRow> LowestRatedPlaces);

public sealed record LowRatedPlaceRow(Guid PlaceId, string Name, bool IsVisible, int ReviewCount, int RatingSum);

public sealed record RatingCountResponse(int Rating, int Count);

public sealed record ReviewQuickTagCountResponse(string Code, int Count);

public sealed record TripFeedbackQuickTagCountResponse(
    [property: JsonConverter(typeof(JsonStringEnumConverter<FeedbackQuickTag>))]
    FeedbackQuickTag QuickTag,
    int Count);

public sealed record AdminReviewSummaryResponse(
    int Count,
    decimal? AverageRating,
    IReadOnlyList<RatingCountResponse> Ratings,
    IReadOnlyList<ReviewQuickTagCountResponse> QuickTags);

public sealed record AdminTripFeedbackSummaryResponse(
    int Count,
    IReadOnlyList<TripFeedbackQuickTagCountResponse> QuickTags);

public sealed record LowRatedPlaceResponse(
    Guid PlaceId,
    string Name,
    bool IsVisible,
    decimal AverageRating,
    int ReviewCount);

public sealed record AdminFeedbackSummaryResponse(
    DateOnly From,
    DateOnly To,
    AdminReviewSummaryResponse Reviews,
    AdminTripFeedbackSummaryResponse TripFeedback,
    IReadOnlyList<LowRatedPlaceResponse> LowestRatedPlaces,
    DateTime GeneratedAt);
