using LocalMateAI.Application.DTOs.PlaceReviews;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IPlaceReviewQueryService
{
    Task<PlaceReviewListResult> GetPlaceReviewsAsync(
        Guid placeId,
        PlaceReviewQuery query,
        CancellationToken cancellationToken = default);
}
