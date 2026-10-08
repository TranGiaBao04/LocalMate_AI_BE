using FluentValidation;
using LocalMateAI.Application.DTOs.PlaceReviews;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class PlaceReviewQueryService(
    IPlaceReviewRepository repository,
    IValidator<PlaceReviewQuery> validator) : IPlaceReviewQueryService
{
    public async Task<PlaceReviewListResult> GetPlaceReviewsAsync(
        Guid placeId,
        PlaceReviewQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return new PlaceReviewListResult(PlaceReviewListResultStatus.InvalidQuery, ValidationErrors: validation.Errors
                .GroupBy(failure => failure.PropertyName)
                .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray()));
        }

        if (!await repository.IsPlaceVisibleAsync(placeId, cancellationToken))
        {
            return new PlaceReviewListResult(PlaceReviewListResultStatus.PlaceNotFound);
        }

        var page = await repository.GetPagedByPlaceAsync(placeId, query, cancellationToken);
        return new PlaceReviewListResult(PlaceReviewListResultStatus.Success, page);
    }
}
