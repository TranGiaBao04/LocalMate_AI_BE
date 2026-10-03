using FluentValidation;
using LocalMateAI.Application.DTOs.Users;
using LocalMateAI.Application.Validators.Common;

namespace LocalMateAI.Application.Validators.Users;

/// <summary>BE-133: luật phân trang chung + bộ lọc trạng thái cho lịch sử chuyến đi.</summary>
public sealed class AdminUserTripQueryValidator : PagedQueryValidator<AdminUserTripQuery>
{
    public AdminUserTripQueryValidator() : base(AdminUserTripQuery.SortFields)
    {
        RuleFor(query => query.Status)
            .IsInEnum()
            .When(query => query.Status.HasValue)
            .WithMessage("Status phải là Draft hoặc Finalized.");
    }
}
