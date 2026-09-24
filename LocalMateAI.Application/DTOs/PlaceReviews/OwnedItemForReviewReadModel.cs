namespace LocalMateAI.Application.DTOs.PlaceReviews;

public sealed record OwnedItemForReviewReadModel(
    Guid ItemId,
    Guid PlaceId,
    bool IsVisited);
