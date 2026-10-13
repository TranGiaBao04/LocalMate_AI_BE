using LocalMateAI.Application.DTOs.Common;

namespace LocalMateAI.Application.DTOs.AdminAiUsage;

public sealed record AdminAiCallsQuery : PagedQuery
{
    public DateOnly? From { get; init; }
    public DateOnly? To { get; init; }
    public string? Kind { get; init; }
    public string? Status { get; init; }
}

public sealed record AdminAiCallReadModel(Guid Id, string Kind, string Status, DateTime StartedAt,
    DateTime? CompletedAt, int? DurationMs, string? Model, int InputTokens, int OutputTokens, Guid? TripId);

public sealed record AdminAiUsageTotals(long Calls, long Succeeded, long ProviderFailed, long InvalidOutput,
    long Abandoned, long InProgress, long InputTokens, long OutputTokens, long DurationSumMs,
    long DurationCount, long ActiveUsers)
{
    public static AdminAiUsageTotals Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
}

public sealed record AdminAiUsageDay(DateOnly Date, long Calls, long Succeeded, long Failed,
    long InputTokens, long OutputTokens, long ActiveUsers);

public sealed record AdminAiUsageKind(string Kind, long Calls, long Succeeded, long InputTokens, long OutputTokens);

public sealed record AdminAiUsageTopUser(Guid UserId, string FullName, string Email, long Calls,
    long InputTokens, long OutputTokens);

// Sparse aggregates: filling missing days/kinds and calculating averages belongs to Application service S2.
public sealed record AdminAiUsageSummaryData(AdminAiUsageTotals Totals, IReadOnlyList<AdminAiUsageDay> Days,
    IReadOnlyList<AdminAiUsageKind> ByKind, IReadOnlyList<AdminAiUsageTopUser> TopUsers);
