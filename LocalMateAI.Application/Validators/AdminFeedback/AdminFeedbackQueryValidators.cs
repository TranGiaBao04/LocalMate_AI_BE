using FluentValidation;
using LocalMateAI.Application.DTOs.AdminFeedback;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.Common;

namespace LocalMateAI.Application.Validators.AdminFeedback;

// Luật phân trang chung + khoảng ngày gửi dùng chung cho hai danh sách.
public abstract class AdminFeedbackListQueryValidator<TQuery> : PagedQueryValidator<TQuery>
    where TQuery : AdminFeedbackListQuery
{
    protected AdminFeedbackListQueryValidator(IReadOnlyCollection<string> sortFields) : base(sortFields)
    {
        RuleFor(query => query.From).Must(IsValidDate).WithMessage(DateMessage("From"));
        RuleFor(query => query.To).Must(IsValidDate).WithMessage(DateMessage("To"));
        RuleFor(query => query.To)
            .Must((query, _) => !TryParse(query.From, out var from) || !TryParse(query.To, out var to) || from <= to)
            .WithMessage("To không được trước From.");
    }

    private static bool IsValidDate(string? value) =>
        string.IsNullOrWhiteSpace(value) || TryParse(value, out _);

    // Giới hạn năm để phép đổi sang UTC và cộng thêm 1 ngày không tràn kiểu ngày.
    private static bool TryParse(string? value, out DateOnly date) =>
        DashboardDateRules.TryParseDate(value, out date)
        && date.Year is >= AdminFeedbackListQuery.MinYear and <= AdminFeedbackListQuery.MaxYear;

    private static string DateMessage(string field) =>
        $"{field} phải có dạng {DashboardDateRules.DateFormat}, trong khoảng năm "
        + $"{AdminFeedbackListQuery.MinYear}–{AdminFeedbackListQuery.MaxYear}.";
}

public sealed class AdminReviewQueryValidator : AdminFeedbackListQueryValidator<AdminReviewQuery>
{
    public AdminReviewQueryValidator() : base(AdminReviewQuery.SortFields)
    {
        RuleFor(query => query.Rating)
            .Must(rating => rating is null
                or (>= PlaceReviewService.MinRating and <= PlaceReviewService.MaxRating))
            .WithMessage($"Rating phải từ {PlaceReviewService.MinRating} đến {PlaceReviewService.MaxRating}.");
    }
}

public sealed class AdminTripFeedbackQueryValidator : AdminFeedbackListQueryValidator<AdminTripFeedbackQuery>
{
    public AdminTripFeedbackQueryValidator() : base(AdminTripFeedbackQuery.SortFields)
    {
        RuleFor(query => query.QuickTag)
            .IsInEnum()
            .When(query => query.QuickTag.HasValue)
            .WithMessage("QuickTag không hợp lệ.");
    }
}
