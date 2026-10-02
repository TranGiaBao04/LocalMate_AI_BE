using FluentValidation;
using LocalMateAI.Application.DTOs.Dashboard;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Application.Validators.Dashboard;

public sealed class DashboardDateRangeQueryValidator : AbstractValidator<DashboardDateRangeQuery>
{
    public DashboardDateRangeQueryValidator(TimeProvider timeProvider)
    {
        RuleFor(query => query.From)
            .Must(BeEmptyOrDate)
            .WithMessage($"From phải có dạng {DashboardDateRules.DateFormat}.");
        RuleFor(query => query.To)
            .Must(BeEmptyOrDate)
            .WithMessage($"To phải có dạng {DashboardDateRules.DateFormat}.");

        RuleFor(query => query).Custom((query, context) =>
        {
            var today = DashboardDateRules.Today(timeProvider);
            if (DashboardDateRules.Resolve(query, today) is not { } range)
            {
                return;
            }

            if (range.To > today)
            {
                context.AddFailure(nameof(query.To), "To không được sau hôm nay.");
            }
            else if (range.From > range.To)
            {
                context.AddFailure(nameof(query.From), "From phải trước hoặc bằng To.");
            }
            else if (range.DayCount > DashboardDateRules.MaxRangeDays)
            {
                context.AddFailure(nameof(query.From), $"Khoảng ngày tối đa {DashboardDateRules.MaxRangeDays} ngày.");
            }
        });
    }

    private static bool BeEmptyOrDate(string? value) =>
        string.IsNullOrWhiteSpace(value) || DashboardDateRules.TryParseDate(value, out _);
}
