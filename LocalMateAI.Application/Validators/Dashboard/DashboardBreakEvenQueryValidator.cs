using FluentValidation;
using LocalMateAI.Application.DTOs.Dashboard;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Application.Validators.Dashboard;

public sealed class DashboardBreakEvenQueryValidator : AbstractValidator<DashboardBreakEvenQuery>
{
    public DashboardBreakEvenQueryValidator(TimeProvider timeProvider)
    {
        RuleFor(query => query.Month).Custom((month, context) =>
        {
            var today = DashboardDateRules.Today(timeProvider);
            if (DashboardDateRules.ResolveMonth(context.InstanceToValidate, today) is not { } firstDay)
            {
                context.AddFailure($"Month phải có dạng {DashboardDateRules.MonthFormat}.");
            }
            else if (firstDay > today)
            {
                context.AddFailure("Month không được ở tương lai.");
            }
            else if (firstDay < DashboardDateRules.MinMonth)
            {
                context.AddFailure($"Month phải từ {DashboardDateRules.MinMonth:yyyy-MM} trở đi.");
            }
        });
    }
}
