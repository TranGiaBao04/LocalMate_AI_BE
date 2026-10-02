using FluentValidation;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Validators.Common;

namespace LocalMateAI.Application.Validators.Places;

/// <summary>
/// BE-93: validator bộ lọc danh sách địa điểm admin kế thừa <see cref="PagedQueryValidator{TQuery}"/>
/// </summary>
public sealed class AdminPlaceQueryValidator : PagedQueryValidator<AdminPlaceQuery>
{
    public AdminPlaceQueryValidator() : base(AdminPlaceQuery.SortFields)
    {
        RuleFor(q => q.Category)
            .IsInEnum()
            .When(q => q.Category.HasValue)
            .WithMessage("Category is invalid.");

        RuleFor(q => q.Status)
            .IsInEnum()
            .When(q => q.Status.HasValue)
            .WithMessage("Status is invalid.");
    }
}
