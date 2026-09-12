using FluentValidation;
using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Validators.Trips;

public sealed class TripRequestValidator : AbstractValidator<TripRequestDto>
{
    public TripRequestValidator()
    {
        RuleFor(x => x.StartLatitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.StartLongitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.DurationHours).InclusiveBetween(1, 24);
        RuleFor(x => x.BudgetMin).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BudgetMax).GreaterThanOrEqualTo(x => x.BudgetMin)
            .WithMessage("BudgetMax phải lớn hơn hoặc bằng BudgetMin.");
        RuleFor(x => x.TagIds).NotNull();
        RuleForEach(x => x.TagIds).NotEmpty();
    }
}
