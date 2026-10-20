using FluentValidation;
using LocalMateAI.Application.DTOs.AppFeedback;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.Common;

namespace LocalMateAI.Application.Validators.AppFeedback;

public sealed class AdminAppFeedbackQueryValidator : PagedQueryValidator<AdminAppFeedbackQuery>
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;
    public const int MinRating = 1;
    public const int MaxRating = 5;

    public AdminAppFeedbackQueryValidator() : base(AdminAppFeedbackQuery.SortFields)
    {
        RuleFor(query => query.Type)
            .IsInEnum()
            .When(query => query.Type.HasValue)
            .WithMessage("Loại góp ý không hợp lệ.");

        RuleFor(query => query.Rating)
            .Must(rating => rating is null or (>= MinRating and <= MaxRating))
            .WithMessage($"Rating phải từ {MinRating} đến {MaxRating}.");

        RuleFor(query => query.From)
            .Must(IsValidDate)
            .WithMessage(DateMessage("From"));

        RuleFor(query => query.To)
            .Must(IsValidDate)
            .WithMessage(DateMessage("To"));

        RuleFor(query => query.To)
            .Must((query, _) => !TryParse(query.From, out var from) || !TryParse(query.To, out var to) || from <= to)
            .WithMessage("To không được trước From.");
    }

    private static bool IsValidDate(string? value) =>
        string.IsNullOrWhiteSpace(value) || TryParse(value, out _);

    private static bool TryParse(string? value, out DateOnly date) =>
        DashboardDateRules.TryParseDate(value, out date)
        && date.Year is >= MinYear and <= MaxYear;

    private static string DateMessage(string field) =>
        $"{field} phải có dạng {DashboardDateRules.DateFormat}, trong khoảng năm {MinYear}–{MaxYear}.";
}
