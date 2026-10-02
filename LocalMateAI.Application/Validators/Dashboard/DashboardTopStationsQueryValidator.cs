using FluentValidation;
using LocalMateAI.Application.DTOs.Dashboard;

namespace LocalMateAI.Application.Validators.Dashboard;

public sealed class DashboardTopStationsQueryValidator : AbstractValidator<DashboardTopStationsQuery>
{
    public DashboardTopStationsQueryValidator(TimeProvider timeProvider)
    {
        Include(new DashboardDateRangeQueryValidator(timeProvider));
        RuleFor(query => query.Limit)
            .InclusiveBetween(1, DashboardTopStationsQuery.MaxLimit)
            .WithMessage($"Limit phải từ 1 đến {DashboardTopStationsQuery.MaxLimit}.");
    }
}
