using FluentValidation;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Application.Validators.Trips;

public sealed class TripRequestValidator : AbstractValidator<TripRequestDto>
{
    public const int MinDurationHours = 1;
    public const int MaxDurationHours = 24;
    public const int MinStationOrder = 1;
    public const int MaxStationOrder = 14;

    public TripRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(x => x.StartLatitude).InclusiveBetween(-90, 90);
        RuleFor(x => x.StartLongitude).InclusiveBetween(-180, 180);
        RuleFor(x => x.StartStationOrder).InclusiveBetween(MinStationOrder, MaxStationOrder);
        RuleFor(x => x.DestinationStationOrder).InclusiveBetween(MinStationOrder, MaxStationOrder);

        // Điểm xuất phát: đủ cặp toạ độ HOẶC một ga, không gửi cả hai.
        RuleFor(x => x).Custom((request, context) =>
        {
            var hasLatitude = request.StartLatitude.HasValue;
            var hasLongitude = request.StartLongitude.HasValue;

            if (request.StartStationOrder.HasValue)
            {
                if (hasLatitude || hasLongitude)
                {
                    context.AddFailure(
                        nameof(TripRequestDto.StartStationOrder),
                        "Chỉ gửi toạ độ xuất phát hoặc ga xuất phát, không gửi cả hai.");
                }

                return;
            }

            if (!hasLatitude && !hasLongitude)
            {
                context.AddFailure(
                    nameof(TripRequestDto.StartLatitude), "Cần toạ độ xuất phát hoặc ga xuất phát.");
            }
            else if (!hasLatitude)
            {
                context.AddFailure(nameof(TripRequestDto.StartLatitude), "Thiếu vĩ độ điểm xuất phát.");
            }
            else if (!hasLongitude)
            {
                context.AddFailure(nameof(TripRequestDto.StartLongitude), "Thiếu kinh độ điểm xuất phát.");
            }
        });
        RuleFor(x => x.DurationHours)
            .InclusiveBetween(MinDurationHours, MaxDurationHours)
            .DependentRules(() => RuleFor(x => x.DurationHours).Custom((hours, context) =>
            {
                var error = TripTimingRules.ValidateWindow(context.InstanceToValidate.StartTime, hours * 60);
                if (error is not null)
                {
                    context.AddFailure(error);
                }
            }));
        RuleFor(x => x.BudgetMin).GreaterThanOrEqualTo(0);
        RuleFor(x => x.BudgetMax).GreaterThanOrEqualTo(x => x.BudgetMin)
            .WithMessage("BudgetMax phải lớn hơn hoặc bằng BudgetMin.");
        RuleFor(x => x.TravelMode).IsInEnum();
        RuleFor(x => x.TagIds).NotNull();
        RuleForEach(x => x.TagIds).NotEmpty();

        RuleFor(x => x.PlannedDate).Custom((plannedDate, context) =>
        {
            var error = TripTimingRules.ValidatePlannedDate(plannedDate, VietnamTime.Now(timeProvider));
            if (error is not null)
            {
                context.AddFailure(error);
            }
        });

        RuleFor(x => x.StartTime).Custom((startTime, context) =>
        {
            var error = TripTimingRules.ValidateStartTime(
                context.InstanceToValidate.PlannedDate, startTime, VietnamTime.Now(timeProvider));
            if (error is not null)
            {
                context.AddFailure(error);
            }
        });
    }
}
