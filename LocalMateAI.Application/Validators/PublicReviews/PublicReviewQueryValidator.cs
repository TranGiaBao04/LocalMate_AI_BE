using FluentValidation;
using LocalMateAI.Application.DTOs.PublicReviews;

namespace LocalMateAI.Application.Validators.PublicReviews;

public sealed class PublicReviewQueryValidator : AbstractValidator<PublicReviewQuery>
{
    public PublicReviewQueryValidator()
    {
        RuleFor(query => query.Limit)
            .InclusiveBetween(1, PublicReviewQuery.MaxLimit)
            .WithMessage($"Limit phải từ 1 đến {PublicReviewQuery.MaxLimit}.");
    }
}
