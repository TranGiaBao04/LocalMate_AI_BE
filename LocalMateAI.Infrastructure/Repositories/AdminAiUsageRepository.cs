using System.Data;
using LocalMateAI.Application.DTOs.AdminAiUsage;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Persistence.Querying;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using NpgsqlTypes;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class AdminAiUsageRepository(AppDbContext context) : IAdminAiUsageRepository
{
    private static readonly SortMap<UsageRow> Sort = new SortMap<UsageRow>("startedAt", true, row => row.Id)
        .Add("startedAt", row => row.StartedAt);

    public Task<bool> UserExistsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        context.Users.AsNoTracking().AnyAsync(user => user.Id == userId, cancellationToken);

    public Task<AdminAiUsageSummaryData> GetSystemSummaryAsync(DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default) => SummaryAsync(null, from, to, cancellationToken);

    public Task<AdminAiUsageSummaryData> GetUserSummaryAsync(Guid userId, DateOnly from, DateOnly to,
        CancellationToken cancellationToken = default) => SummaryAsync(userId, from, to, cancellationToken);

    public async Task<PagedResult<AdminAiCallReadModel>> GetUserCallsAsync(Guid userId, AdminAiCallsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", cancellationToken);
        var rows = Rows(query.From, query.To).Where(row => row.UserId == userId);
        if (query.Kind is not null) rows = rows.Where(row => row.Kind == query.Kind);
        if (query.Status is not null) rows = rows.Where(row => row.Status == query.Status);
        // Search deliberately has no effect. SourceRank is the final tie-break only, never returned.
        var page = await rows.ApplySort(query, Sort).ThenBy(row => row.SourceRank)
            .Select(row => new AdminAiCallReadModel(row.Id, row.Kind, row.Status, row.StartedAt,
                row.CompletedAt, row.DurationMs, row.Model, row.InputTokens, row.OutputTokens, row.TripId))
            .ToPagedResultAsync(query, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return page;
    }

    private async Task<AdminAiUsageSummaryData> SummaryAsync(Guid? userId, DateOnly from, DateOnly to,
        CancellationToken ct)
    {
        if (from > to || to.DayNumber - from.DayNumber >= 366)
            throw new ArgumentOutOfRangeException(nameof(to), "Summary requires an ordered range of at most 366 days.");
        await using var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
        await context.Database.ExecuteSqlRawAsync("SET TRANSACTION READ ONLY", ct);
        var rows = Rows(from, to);
        if (userId.HasValue) rows = rows.Where(row => row.UserId == userId.Value);
        var totals = await rows.GroupBy(_ => 1).Select(group => new AdminAiUsageTotals(
            group.LongCount(), group.LongCount(row => row.Status == "Succeeded"),
            group.LongCount(row => row.Status == "ProviderFailed"), group.LongCount(row => row.Status == "InvalidOutput"),
            group.LongCount(row => row.Status == "Abandoned"), group.LongCount(row => row.Status == "InProgress"),
            group.Sum(row => (long)row.InputTokens), group.Sum(row => (long)row.OutputTokens),
            group.Sum(row => (long)(row.DurationMs ?? 0)), group.LongCount(row => row.DurationMs != null),
            group.Select(row => row.UserId).Distinct().LongCount())).SingleOrDefaultAsync(ct)
            ?? AdminAiUsageTotals.Empty;
        var days = await rows.GroupBy(row => row.VietnamUsageDate).OrderBy(group => group.Key)
            .Select(group => new AdminAiUsageDay(group.Key, group.LongCount(),
                group.LongCount(row => row.Status == "Succeeded"),
                group.LongCount(row => row.Status == "ProviderFailed" || row.Status == "InvalidOutput" || row.Status == "Abandoned"),
                group.Sum(row => (long)row.InputTokens), group.Sum(row => (long)row.OutputTokens),
                group.Select(row => row.UserId).Distinct().LongCount())).ToListAsync(ct);
        var kinds = await rows.GroupBy(row => row.Kind).OrderBy(group => group.Key)
            .Select(group => new AdminAiUsageKind(group.Key, group.LongCount(),
                group.LongCount(row => row.Status == "Succeeded"), group.Sum(row => (long)row.InputTokens),
                group.Sum(row => (long)row.OutputTokens))).ToListAsync(ct);
        IReadOnlyList<AdminAiUsageTopUser> top = [];
        if (!userId.HasValue)
        {
            var users = rows.GroupBy(row => row.UserId).Select(group => new
            {
                UserId = group.Key, Calls = group.LongCount(), InputTokens = group.Sum(row => (long)row.InputTokens),
                OutputTokens = group.Sum(row => (long)row.OutputTokens)
            }).OrderByDescending(row => row.Calls).ThenBy(row => row.UserId).Take(10);
            top = await users.Join(context.Users.AsNoTracking(), row => row.UserId, user => user.Id,
                (row, user) => new { user.Id, user.FullName, user.Email, row.Calls, row.InputTokens, row.OutputTokens })
                .OrderByDescending(row => row.Calls).ThenBy(row => row.Id)
                .Select(row => new AdminAiUsageTopUser(row.Id, row.FullName, row.Email, row.Calls,
                    row.InputTokens, row.OutputTokens)).ToListAsync(ct);
        }
        await transaction.CommitAsync(ct);
        return new(totals, days, kinds, top);
    }

    private IQueryable<UsageRow> Rows(DateOnly? from, DateOnly? to)
    {
        if (from.HasValue && to.HasValue && from > to) throw new ArgumentException("From must not exceed To.");
        // Only omit the unrepresentable bound, not the other side of the requested range.
        DateTime? lower = from.HasValue && from.Value != DateOnly.MinValue
            ? from.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-7) : null;
        DateTime? upper = to.HasValue && to.Value != DateOnly.MaxValue
            ? to.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc).AddHours(-7) : null;
        return context.Database.SqlQueryRaw<UsageRow>("""
            SELECT a."Id", 0 AS "SourceRank", a."UserId", a."Kind",
                CASE WHEN a."State" = 'Completed' THEN a."Outcome"
                     WHEN a."State" = 'Abandoned' THEN 'Abandoned' ELSE 'InProgress' END AS "Status",
                a."VietnamUsageDate", a."AdmittedAt" AS "StartedAt", a."CompletedAt",
                CASE WHEN a."State" = 'Completed' THEN l."InputTokens" ELSE 0 END AS "InputTokens",
                CASE WHEN a."State" = 'Completed' THEN l."OutputTokens" ELSE 0 END AS "OutputTokens",
                CASE WHEN a."State" = 'Completed' THEN l."DurationMilliseconds" ELSE NULL END AS "DurationMs",
                CASE WHEN a."State" = 'Completed' THEN l."Model" ELSE NULL END AS "Model", a."TripId"
            FROM "AiUsageAdmissions" a LEFT JOIN "LlmCallLogs" l ON l."Id" = a."LlmCallLogId"
            WHERE a."State" <> 'Released'
                AND (@from::date IS NULL OR a."VietnamUsageDate" >= @from)
                AND (@to::date IS NULL OR a."VietnamUsageDate" <= @to)
            UNION ALL
            SELECT l."Id", 1 AS "SourceRank", l."UserId", l."Kind", l."Outcome" AS "Status",
                (l."CreatedAt" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date AS "VietnamUsageDate",
                l."CreatedAt" AS "StartedAt", l."CreatedAt" AS "CompletedAt",
                l."InputTokens", l."OutputTokens", l."DurationMilliseconds" AS "DurationMs", l."Model", l."TripId"
            FROM "LlmCallLogs" l
            WHERE NOT EXISTS (SELECT 1 FROM "AiUsageAdmissions" a WHERE a."LlmCallLogId" = l."Id")
                AND (@lower::timestamptz IS NULL OR l."CreatedAt" >= @lower)
                AND (@upper::timestamptz IS NULL OR l."CreatedAt" < @upper)
            """, Parameter("from", NpgsqlDbType.Date, from), Parameter("to", NpgsqlDbType.Date, to),
            Parameter("lower", NpgsqlDbType.TimestampTz, lower), Parameter("upper", NpgsqlDbType.TimestampTz, upper));
    }

    private static NpgsqlParameter Parameter(string name, NpgsqlDbType type, object? value) =>
        new(name, type) { Value = value ?? DBNull.Value };

    // Non-entity SQL projection; no DbSet/model configuration and no tracking or schema changes.
    private sealed class UsageRow
    {
        public Guid Id { get; set; }
        public int SourceRank { get; set; }
        public Guid UserId { get; set; }
        public string Kind { get; set; } = null!;
        public string Status { get; set; } = null!;
        public DateOnly VietnamUsageDate { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public int InputTokens { get; set; }
        public int OutputTokens { get; set; }
        public int? DurationMs { get; set; }
        public string? Model { get; set; }
        public Guid? TripId { get; set; }
    }
}
