using LocalMateAI.Application.DTOs.PublicReviews;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IPublicReviewService
{
    Task<PublicReviewsResult> GetAsync(PublicReviewQuery query, CancellationToken cancellationToken = default);
}
