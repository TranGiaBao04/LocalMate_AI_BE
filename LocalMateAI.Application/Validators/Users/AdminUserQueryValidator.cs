using FluentValidation;
using LocalMateAI.Application.DTOs.Users;
using LocalMateAI.Application.Validators.Common;

namespace LocalMateAI.Application.Validators.Users;

/// <summary>BE-132: luật phân trang chung + bộ lọc riêng của danh sách user admin.</summary>
public sealed class AdminUserQueryValidator : PagedQueryValidator<AdminUserQuery>
{
    public AdminUserQueryValidator() : base(AdminUserQuery.SortFields)
    {
        RuleFor(query => query.Status)
            .IsInEnum()
            .When(query => query.Status.HasValue)
            .WithMessage("Status phải là Active hoặc Locked.");

        RuleFor(query => query.Plan)
            .Must(plan => plan is null || plan.Trim().Length <= AdminUserQuery.MaxPlanLength)
            .WithMessage($"Plan tối đa {AdminUserQuery.MaxPlanLength} ký tự.");
    }
}
