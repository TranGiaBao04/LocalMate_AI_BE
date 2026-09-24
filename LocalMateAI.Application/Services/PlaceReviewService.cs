using LocalMateAI.Application.DTOs.PlaceReviews;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Constants;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Services;

public sealed class PlaceReviewService(
    IUserRepository userRepository,
    IPlaceReviewRepository reviewRepository) : IPlaceReviewService
{
    private const int MinRating = 1;
    private const int MaxRating = 5;
    private const int MaxQuickTags = 3;
    private const int MaxCommentLength = 1000;

    public async Task<CreatePlaceReviewResult> CreateAsync(
        Guid userId,
        Guid itemId,
        CreatePlaceReviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (itemId == Guid.Empty)
        {
            return CreatePlaceReviewResult.InvalidItem();
        }

        var quickTags = (request.QuickTags ?? []).Distinct(StringComparer.Ordinal).ToArray();
        var comment = string.IsNullOrWhiteSpace(request.Comment) ? null : request.Comment.Trim();

        var validationErrors = Validate(request.Rating, quickTags, comment);
        if (validationErrors.Count > 0)
        {
            return CreatePlaceReviewResult.Invalid(validationErrors);
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return CreatePlaceReviewResult.MissingUser();
        }

        var item = await reviewRepository.GetOwnedItemAsync(itemId, userId, cancellationToken);
        if (item is null)
        {
            return CreatePlaceReviewResult.MissingItem();
        }

        // Chỉ đánh dấu "đã ghé" được trên trip Finalized nên chỉ cần kiểm tra IsVisited.
        if (!item.IsVisited)
        {
            return CreatePlaceReviewResult.NotVisited();
        }

        if (await reviewRepository.ExistsAsync(userId, itemId, cancellationToken))
        {
            return CreatePlaceReviewResult.Duplicate();
        }

        var review = new PlaceReview
        {
            UserId = userId,
            PlaceId = item.PlaceId,
            ItineraryItemId = itemId,
            Rating = request.Rating,
            QuickTags = quickTags,
            Comment = comment
        };

        return await reviewRepository.TryAddAsync(review, cancellationToken)
            ? CreatePlaceReviewResult.Succeeded(Map(review))
            : CreatePlaceReviewResult.Duplicate();
    }

    public async Task<GetPlaceReviewResult> GetAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        if (itemId == Guid.Empty)
        {
            return GetPlaceReviewResult.InvalidItem();
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return GetPlaceReviewResult.MissingUser();
        }

        var item = await reviewRepository.GetOwnedItemAsync(itemId, userId, cancellationToken);
        if (item is null)
        {
            return GetPlaceReviewResult.MissingItem();
        }

        var review = await reviewRepository.GetAsync(userId, itemId, cancellationToken);
        return review is null
            ? GetPlaceReviewResult.MissingReview()
            : GetPlaceReviewResult.Succeeded(Map(review));
    }

    private static Dictionary<string, string[]> Validate(int rating, string[] quickTags, string? comment)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);

        if (rating is < MinRating or > MaxRating)
        {
            errors["Rating"] = [$"Rating must be between {MinRating} and {MaxRating}."];
        }

        if (quickTags.Length > MaxQuickTags)
        {
            errors["QuickTags"] = [$"At most {MaxQuickTags} quick tags are allowed."];
        }
        else if (quickTags.Any(tag => tag is null || !ReviewQuickTags.All.Contains(tag)))
        {
            errors["QuickTags"] = ["One or more quick tags are invalid."];
        }

        if (comment is { Length: > MaxCommentLength })
        {
            errors["Comment"] = [$"Comment must not exceed {MaxCommentLength} characters."];
        }

        return errors;
    }

    private static PlaceReviewResponse Map(PlaceReview review) =>
        new(
            review.Id,
            review.ItineraryItemId,
            review.PlaceId,
            review.Rating,
            review.QuickTags,
            review.Comment,
            review.CreatedAt);
}
