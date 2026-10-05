using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Interfaces.Services;

public enum AiUsageStatus
{
    Allowed,
    Disabled,
    DailyLimitReached,
    TripLimitReached
}

/// <param name="ResetAtUtc">Thời điểm trần ngày được đặt lại; chỉ có khi DailyLimitReached.</param>
public sealed record AiUsageDecision(AiUsageStatus Status, DateTime? ResetAtUtc = null);

public interface IAiUsageGuard
{
    /// <summary>Kiểm tra trước khi gọi LLM: tính năng có bật không, người dùng còn lượt không.</summary>
    Task<AiUsageDecision> CheckAsync(
        Guid userId,
        LlmCallKind kind,
        Guid? tripId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ghi lại một lần đã gọi LLM, kể cả khi lỗi: lần nào đã gửi đi cũng tính vào trần NGÀY.
    /// Trần theo chuyến đi chỉ tính lần thành công.
    /// </summary>
    Task RecordAsync(
        Guid userId,
        LlmCallKind kind,
        Guid? tripId,
        LlmCallOutcome outcome,
        LlmJsonResponse? response,
        int durationMilliseconds,
        CancellationToken cancellationToken = default);
}
