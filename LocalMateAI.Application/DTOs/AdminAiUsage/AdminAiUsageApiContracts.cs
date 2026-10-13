using LocalMateAI.Application.DTOs.Common;

namespace LocalMateAI.Application.DTOs.AdminAiUsage;

// HTTP input stays textual so malformed dates/enum names can become field-level validation errors.
public sealed record AdminAiCallsRequest : PagedQuery
{
    public AdminAiCallsRequest() { SortBy = "startedAt"; SortDirection = Descending; }
    public string? From { get; init; }
    public string? To { get; init; }
    public string? Kind { get; init; }
    public string? Status { get; init; }
}

public enum AdminAiUsageResultStatus { Success, InvalidQuery, UserNotFound }

public sealed record AdminAiUsageResult<T>(AdminAiUsageResultStatus Status, T? Response = default,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null);

public sealed record AdminAiUsageTotalsResponse(long Calls, long Succeeded, long ProviderFailed, long InvalidOutput,
    long Abandoned, long InProgress, long InputTokens, long OutputTokens, double? AverageDurationMs);

public sealed record AdminAiUsageSystemTotalsResponse(long Calls, long Succeeded, long ProviderFailed, long InvalidOutput,
    long Abandoned, long InProgress, long InputTokens, long OutputTokens, double? AverageDurationMs, long ActiveUsers);

public sealed record AdminAiUsageDayResponse(DateOnly Date, long Calls, long Succeeded, long Failed,
    long InputTokens, long OutputTokens);

public sealed record AdminAiUsageSystemDayResponse(DateOnly Date, long Calls, long Succeeded, long Failed,
    long InputTokens, long OutputTokens, long ActiveUsers);

public sealed record AdminAiUsageTodayResponse(int Used, int Limit, DateTime ResetAt);

public sealed record AdminAiUsageSystemResponse(DateOnly From, DateOnly To, AdminAiUsageSystemTotalsResponse Totals,
    IReadOnlyList<AdminAiUsageKind> ByKind, IReadOnlyList<AdminAiUsageSystemDayResponse> Days,
    IReadOnlyList<AdminAiUsageTopUser> TopUsers, DateTime GeneratedAt);

public sealed record AdminAiUsageUserResponse(Guid UserId, DateOnly From, DateOnly To, AdminAiUsageTodayResponse Today,
    AdminAiUsageTotalsResponse Totals, IReadOnlyList<AdminAiUsageDayResponse> Days, DateTime GeneratedAt);
