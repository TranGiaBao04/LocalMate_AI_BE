namespace LocalMateAI.Application.DTOs.PlaceReviews;

public sealed record PlaceReviewResponse(
    Guid Id,
    Guid ItineraryItemId,
    Guid PlaceId,
    int Rating,
    IReadOnlyList<string> QuickTags,
    string? Comment,
    DateTime CreatedAt);
