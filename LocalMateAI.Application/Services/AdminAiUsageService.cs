using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using LocalMateAI.Application.DTOs.AdminAiUsage;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Dashboard;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;

namespace LocalMateAI.Application.Services;

public sealed class AdminAiUsageService(IAdminAiUsageRepository repository, ISubscriptionService subscriptions,
    IValidator<DashboardDateRangeQuery> rangeValidator, IValidator<AdminAiCallsRequest> callsValidator,
    IMemoryCache cache, TimeProvider clock) : IAdminAiUsageService
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<AdminAiUsageResult<AdminAiUsageSystemResponse>> GetSummaryAsync(DashboardDateRangeQuery query,
        CancellationToken cancellationToken = default)
    {
        var errors = await ValidateRangeAsync(query, cancellationToken);
        if (errors.Count > 0) return Invalid<AdminAiUsageSystemResponse>(errors);
        var range = DashboardDateRules.Resolve(query, DashboardDateRules.Today(clock))!;
        var key = string.Create(CultureInfo.InvariantCulture, $"admin-ai-usage:summary:{range.From:yyyy-MM-dd}:{range.To:yyyy-MM-dd}");
        if (cache.TryGetValue(key, out AdminAiUsageSystemResponse? cached) && cached is not null)
            return new(AdminAiUsageResultStatus.Success, cached);
        var data = await repository.GetSystemSummaryAsync(range.From, range.To, cancellationToken);
        var totals = data.Totals;
        var kinds = data.ByKind.ToDictionary(row => row.Kind);
        var response = new AdminAiUsageSystemResponse(range.From, range.To,
            new(totals.Calls, totals.Succeeded, totals.ProviderFailed, totals.InvalidOutput, totals.Abandoned,
                totals.InProgress, totals.InputTokens, totals.OutputTokens, Average(totals), totals.ActiveUsers),
            new[] { "ParseRequest", "Explain" }.Select(kind => kinds.TryGetValue(kind, out var row)
                ? row : new AdminAiUsageKind(kind, 0, 0, 0, 0)).ToArray(),
            Days(range, data.Days).Select(day => new AdminAiUsageSystemDayResponse(day.Date, day.Calls, day.Succeeded,
                day.Failed, day.InputTokens, day.OutputTokens, day.ActiveUsers)).ToArray(),
            data.TopUsers.Take(10).ToArray(), clock.GetUtcNow().UtcDateTime);
        cache.Set(key, response, CacheDuration);
        return new(AdminAiUsageResultStatus.Success, response);
    }

    public async Task<AdminAiUsageResult<AdminAiUsageUserResponse>> GetUserSummaryAsync(Guid userId,
        DashboardDateRangeQuery query, CancellationToken cancellationToken = default)
    {
        var errors = await ValidateRangeAsync(query, cancellationToken);
        if (errors.Count > 0) return Invalid<AdminAiUsageUserResponse>(errors);
        if (!await repository.UserExistsAsync(userId, cancellationToken)) return new(AdminAiUsageResultStatus.UserNotFound);
        var range = DashboardDateRules.Resolve(query, DashboardDateRules.Today(clock))!;
        var data = await repository.GetUserSummaryAsync(userId, range.From, range.To, cancellationToken);
        var subscription = await subscriptions.GetMySubscriptionAsync(userId, cancellationToken);
        if (subscription is null) return new(AdminAiUsageResultStatus.UserNotFound);
        var totals = data.Totals;
        return new(AdminAiUsageResultStatus.Success, new(userId, range.From, range.To,
            new(subscription.Ai.DailyUsed, subscription.Ai.DailyLimit, subscription.Ai.ResetAt),
            new(totals.Calls, totals.Succeeded, totals.ProviderFailed, totals.InvalidOutput, totals.Abandoned,
                totals.InProgress, totals.InputTokens, totals.OutputTokens, Average(totals)),
            Days(range, data.Days).Select(day => new AdminAiUsageDayResponse(day.Date, day.Calls, day.Succeeded,
                day.Failed, day.InputTokens, day.OutputTokens)).ToArray(), clock.GetUtcNow().UtcDateTime));
    }

    public async Task<AdminAiUsageResult<PagedResult<AdminAiCallReadModel>>> GetUserCallsAsync(Guid userId,
        AdminAiCallsRequest query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var validation = await callsValidator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid) return Invalid<PagedResult<AdminAiCallReadModel>>(validation.Errors);
        if (!await repository.UserExistsAsync(userId, cancellationToken)) return new(AdminAiUsageResultStatus.UserNotFound);
        var typed = new AdminAiCallsQuery
        {
            Page = query.Page, PageSize = query.PageSize,
            SortBy = "startedAt",
            SortDirection = Normalize(query.SortDirection)?.ToLowerInvariant() ?? PagedQuery.Descending,
            From = DashboardDateRules.TryParseDate(query.From, out var from) ? from : null,
            To = DashboardDateRules.TryParseDate(query.To, out var to) ? to : null,
            Kind = Normalize(query.Kind), Status = Normalize(query.Status), Search = null
        };
        return new(AdminAiUsageResultStatus.Success, await repository.GetUserCallsAsync(userId, typed, cancellationToken));
    }

    private async Task<List<ValidationFailure>> ValidateRangeAsync(DashboardDateRangeQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        try { return (await rangeValidator.ValidateAsync(query, ct)).Errors; }
        catch (ArgumentOutOfRangeException) when (string.IsNullOrWhiteSpace(query.From)
            && DashboardDateRules.TryParseDate(query.To, out var to) && to.DayNumber < DashboardDateRules.DefaultRangeDays - 1)
        {
            // A full default 30-day range cannot precede DateOnly.MinValue; explicit valid ranges remain supported.
            return [new(nameof(query.From), "From is required when the default 30-day range would exceed the supported date boundary.")];
        }
    }

    private static IEnumerable<AdminAiUsageDay> Days(DashboardDateRange range, IReadOnlyList<AdminAiUsageDay> rows)
    {
        var dates = rows.ToDictionary(row => row.Date);
        return Enumerable.Range(0, range.DayCount).Select(offset => range.From.AddDays(offset))
            .Select(date => dates.TryGetValue(date, out var row) ? row : new AdminAiUsageDay(date, 0, 0, 0, 0, 0, 0));
    }

    private static double? Average(AdminAiUsageTotals totals) => totals.DurationCount > 0
        ? (double)totals.DurationSumMs / totals.DurationCount : null;
    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static AdminAiUsageResult<T> Invalid<T>(IEnumerable<ValidationFailure> failures) =>
        new(AdminAiUsageResultStatus.InvalidQuery, ValidationErrors: failures.GroupBy(error => error.PropertyName)
            .ToDictionary(group => group.Key, group => group.Select(error => error.ErrorMessage).Distinct().ToArray()));
}
