using LocalMateAI.Application.DTOs.PlaceReviews;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IPlaceReviewService
{
    Task<CreatePlaceReviewResult> CreateAsync(
        Guid userId,
        Guid itemId,
        CreatePlaceReviewRequest request,
        CancellationToken cancellationToken = default);

    Task<GetPlaceReviewResult> GetAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default);
}
