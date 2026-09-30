using FluentValidation;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Plans;
using LocalMateAI.Application.Validators.Common;

namespace LocalMateAI.Application.Validators.Plans;

public sealed class AdminPlanQueryValidator() : PagedQueryValidator<AdminPlanQuery>(AdminPlanQuery.SortFields);

public sealed class AdminPlanVersionsQueryValidator : PagedQueryValidator<AdminPlanVersionsQuery>
{
    public AdminPlanVersionsQueryValidator() : base(["versionNumber"])
    {
        RuleFor(q => q.SortDirection).Must(d => string.IsNullOrWhiteSpace(d)
            || string.Equals(d.Trim(), PagedQuery.Descending, StringComparison.OrdinalIgnoreCase))
            .WithMessage("Version history is ordered by versionNumber descending.");
        RuleFor(q => q.Search).Must(string.IsNullOrWhiteSpace)
            .WithMessage("Search is not supported for version history.");
    }
}
