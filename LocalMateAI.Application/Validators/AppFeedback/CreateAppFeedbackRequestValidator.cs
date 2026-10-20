using FluentValidation;
using LocalMateAI.Application.DTOs.AppFeedback;

namespace LocalMateAI.Application.Validators.AppFeedback;

public sealed class CreateAppFeedbackRequestValidator : AbstractValidator<CreateAppFeedbackRequest>
{
    public const int MinContentLength = 1;
    public const int MaxContentLength = 1000;
    public const int MinRating = 1;
    public const int MaxRating = 5;

    public CreateAppFeedbackRequestValidator()
    {
        RuleFor(x => x.Type)
            .IsInEnum()
            .WithMessage("Loại góp ý không hợp lệ.");

        RuleFor(x => x.Rating)
            .Must(r => r is null or (>= MinRating and <= MaxRating))
            .WithMessage($"Rating phải từ {MinRating} đến {MaxRating}.");

        RuleFor(x => x.Content)
            .Cascade(CascadeMode.Stop)
            .NotEmpty()
            .WithMessage("Nội dung góp ý không được để trống.")
            .Must(c => !string.IsNullOrWhiteSpace(c) && c.Trim().Length is >= MinContentLength and <= MaxContentLength)
            .WithMessage($"Nội dung góp ý phải từ {MinContentLength} đến {MaxContentLength} ký tự sau khi cắt khoảng trắng.");
    }
}
