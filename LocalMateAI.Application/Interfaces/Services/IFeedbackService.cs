using LocalMateAI.Application.DTOs.Feedback;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IFeedbackService
{
    Task<CreateFeedbackResult> CreateAsync(
        Guid currentUserId,
        CreateFeedbackRequest request,
        CancellationToken cancellationToken = default);

    Task<GetFeedbackResult> GetAsync(
        Guid currentUserId,
        Guid tripId,
        CancellationToken cancellationToken = default);
}
