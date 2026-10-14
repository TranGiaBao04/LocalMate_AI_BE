namespace LocalMateAI.Application.DTOs.Subscription;

// Mọi field để nullable để thiếu field nào thì báo lỗi đúng field đó.
public sealed record SaveTrialProgramRequest(
    string? Name,
    DateOnly? StartsOn,
    DateOnly? EndsOn,
    int? TrialDays,
    int? MaxRecipients,
    bool? IsEnabled);

public enum TrialProgramStatus
{
    Disabled,
    Ended,
    Upcoming,
    Full,
    Running
}

/// <summary><c>Remaining</c> là null khi chương trình không giới hạn số người nhận.</summary>
public sealed record TrialProgramResponse(
    Guid Id,
    string Name,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int TrialDays,
    int? MaxRecipients,
    bool IsEnabled,
    TrialProgramStatus Status,
    int GrantedCount,
    int? Remaining,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record TrialProgramReadModel(
    Guid Id,
    string Name,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int TrialDays,
    int? MaxRecipients,
    bool IsEnabled,
    int GrantedCount,
    DateTime CreatedAt,
    DateTime UpdatedAt);

// Nội dung đã kiểm tra và chuẩn hoá, sẵn sàng để lưu.
public sealed record TrialProgramDraft(
    string Name,
    DateOnly StartsOn,
    DateOnly EndsOn,
    int TrialDays,
    int? MaxRecipients,
    bool IsEnabled);

public enum TrialProgramResultStatus
{
    Success,
    ValidationFailed,
    NotFound,
    Overlap
}

public sealed record TrialProgramResult(
    TrialProgramResultStatus Status,
    TrialProgramResponse? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);
