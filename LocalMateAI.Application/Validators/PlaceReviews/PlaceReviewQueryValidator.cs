using FluentValidation;
using LocalMateAI.Application.DTOs.PlaceReviews;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.Common;

namespace LocalMateAI.Application.Validators.PlaceReviews;

public sealed class PlaceReviewQueryValidator : PagedQueryValidator<PlaceReviewQuery>
{
    public PlaceReviewQueryValidator() : base(PlaceReviewQuery.SortFields)
    {
        RuleFor(query => query.Rating)
            .Must(rating => rating is null
                or (>= PlaceReviewService.MinRating and <= PlaceReviewService.MaxRating))
            .WithMessage($"Rating phải từ {PlaceReviewService.MinRating} đến {PlaceReviewService.MaxRating}.");
    }
}
