using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.AppFeedback;

public sealed record CreateAppFeedbackRequest(
    AppFeedbackType Type,
    int? Rating,
    string Content,
    string? PagePath);

public sealed record AppFeedbackCreatedResponse(
    Guid Id,
    DateTime CreatedAt);

public sealed record AppFeedbackUserDto(
    Guid UserId,
    string FullName,
    string Email);

public sealed record AdminAppFeedbackItemResponse(
    Guid Id,
    AppFeedbackType Type,
    int? Rating,
    string Content,
    string? PagePath,
    string? UserAgent,
    DateTime CreatedAt,
    AppFeedbackUserDto User);

public record AdminAppFeedbackQuery : PagedQuery
{
    public static readonly IReadOnlyCollection<string> SortFields = ["createdAt", "rating"];

    public AppFeedbackType? Type { get; init; }
    public int? Rating { get; init; }
    public Guid? UserId { get; init; }
    public string? From { get; init; }
    public string? To { get; init; }
}

public enum CreateAppFeedbackResultStatus
{
    Success,
    InvalidInput,
    DailyLimitReached,
    DemoSessionForbidden
}

public sealed record CreateAppFeedbackResult(
    CreateAppFeedbackResultStatus Status,
    AppFeedbackCreatedResponse? Response = null,
    DateTime? ResetAt = null,
    IDictionary<string, string[]>? ValidationErrors = null);
