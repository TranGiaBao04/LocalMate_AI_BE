using LocalMateAI.Application.DTOs.Feedback;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class FeedbackService(
    IFeedbackRepository feedbackRepository,
    IUserRepository userRepository) : IFeedbackService
{
    private const int MaximumCommentLength = 1000;

    public async Task<CreateFeedbackResult> CreateAsync(
        Guid currentUserId,
        CreateFeedbackRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationErrors = Validate(request);
        if (validationErrors.Count > 0)
        {
            return CreateFeedbackResult.ValidationFailed(validationErrors);
        }

        var user = await userRepository.GetByIdAsync(currentUserId, cancellationToken);
        if (user is null)
        {
            return CreateFeedbackResult.MissingPersistedUser();
        }

        var trip = await feedbackRepository.GetOwnedTripAsync(
            request.TripId,
            currentUserId,
            cancellationToken);
        if (trip is null)
        {
            return CreateFeedbackResult.MissingTrip();
        }

        if (trip.Status != TripStatus.Finalized)
        {
            return CreateFeedbackResult.NotFinalized();
        }

        if (await feedbackRepository.ExistsAsync(currentUserId, request.TripId, cancellationToken))
        {
            return CreateFeedbackResult.AlreadyExists();
        }

        var feedback = new Feedback
        {
            UserId = currentUserId,
            TripId = request.TripId,
            QuickTag = request.QuickTag,
            Comment = request.Comment
        };

        var added = await feedbackRepository.TryAddAsync(feedback, cancellationToken);
        if (!added)
        {
            return CreateFeedbackResult.AlreadyExists();
        }

        return CreateFeedbackResult.Succeeded(new FeedbackResponse(
            feedback.Id,
            feedback.TripId,
            feedback.QuickTag,
            feedback.Comment,
            feedback.CreatedAt));
    }

    private static Dictionary<string, string[]> Validate(CreateFeedbackRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        if (request.TripId == Guid.Empty)
        {
            errors[nameof(request.TripId)] = ["TripId must be a valid GUID."];
        }

        if (!Enum.IsDefined(request.QuickTag))
        {
            errors[nameof(request.QuickTag)] = ["QuickTag is invalid."];
        }

        if (request.Comment?.Length > MaximumCommentLength)
        {
            errors[nameof(request.Comment)] = ["Comment must not exceed 1000 characters."];
        }

        return errors;
    }
}
