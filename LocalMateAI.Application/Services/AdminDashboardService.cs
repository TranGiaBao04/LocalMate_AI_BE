using System.Globalization;
using FluentValidation;
using FluentValidation.Results;
using LocalMateAI.Application.Dashboard;
using LocalMateAI.Application.DTOs.Dashboard;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Application.Services;

public sealed class AdminDashboardService(
    IAdminDashboardRepository repository,
    IValidator<DashboardDateRangeQuery> rangeValidator,
    IValidator<DashboardTopStationsQuery> topStationsValidator,
    IValidator<DashboardBreakEvenQuery> breakEvenValidator,
    IOptions<DashboardOptions> options,
    IMemoryCache cache,
    TimeProvider timeProvider) : IAdminDashboardService
{
    public const string Currency = "VND";
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<AdminDashboardResult<AdminDashboardSummaryResponse>> GetSummaryAsync(
        DashboardDateRangeQuery query, CancellationToken cancellationToken = default)
    {
        var validation = await rangeValidator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return Invalid<AdminDashboardSummaryResponse>(validation.Errors);
        }

        var range = DashboardDateRules.Resolve(query, DashboardDateRules.Today(timeProvider))!;
        var response = await GetOrCreateAsync(CacheKey("summary", range), async () =>
        {
            var counts = await repository.GetSummaryAsync(range.StartUtc, range.EndUtc, cancellationToken);
            return new AdminDashboardSummaryResponse(range.From, range.To, counts.NewUsers, counts.TripsCreated,
                counts.PaidOrders, counts.Revenue, Currency, timeProvider.GetUtcNow().UtcDateTime);
        });

        return new(AdminDashboardResultStatus.Success, response);
    }

    public async Task<AdminDashboardResult<AdminDashboardRevenueDailyResponse>> GetRevenueDailyAsync(
        DashboardDateRangeQuery query, CancellationToken cancellationToken = default)
    {
        var validation = await rangeValidator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return Invalid<AdminDashboardRevenueDailyResponse>(validation.Errors);
        }

        var range = DashboardDateRules.Resolve(query, DashboardDateRules.Today(timeProvider))!;
        var response = await GetOrCreateAsync(CacheKey("revenue-daily", range), async () =>
        {
            var rows = (await repository.GetDailyRevenueAsync(range.StartUtc, range.EndUtc, cancellationToken))
                .ToDictionary(row => row.Date);
            // Ngày không có đơn vẫn có dòng 0đ để FE vẽ biểu đồ liền mạch.
            var days = Enumerable.Range(0, range.DayCount)
                .Select(offset => range.From.AddDays(offset))
                .Select(date => rows.TryGetValue(date, out var row)
                    ? new DailyRevenueResponse(date, row.Revenue, row.PaidOrders)
                    : new DailyRevenueResponse(date, 0m, 0))
                .ToList();

            return new AdminDashboardRevenueDailyResponse(range.From, range.To, Currency,
                days.Sum(day => day.Revenue), days, timeProvider.GetUtcNow().UtcDateTime);
        });

        return new(AdminDashboardResultStatus.Success, response);
    }

    public async Task<AdminDashboardResult<AdminDashboardTopStationsResponse>> GetTopStationsAsync(
        DashboardTopStationsQuery query, CancellationToken cancellationToken = default)
    {
        var validation = await topStationsValidator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return Invalid<AdminDashboardTopStationsResponse>(validation.Errors);
        }

        var range = DashboardDateRules.Resolve(query, DashboardDateRules.Today(timeProvider))!;
        var response = await GetOrCreateAsync(CacheKey($"top-stations:{query.Limit}", range), async () =>
        {
            var rows = await repository.GetTripCountsByStationAsync(range.StartUtc, range.EndUtc, cancellationToken);
            // Mọi trip đều gom được về một ga ⇒ tổng các ga = tổng trip trong khoảng.
            var totalTrips = rows.Sum(row => row.TripCount);
            var stations = rows
                .OrderByDescending(row => row.TripCount)
                .ThenBy(row => row.StationOrder)
                .Take(query.Limit)
                .Select(row => new TopStationResponse(row.StationOrder, row.StationName, row.TripCount,
                    Math.Round((decimal)row.TripCount / totalTrips * 100m, 1, MidpointRounding.AwayFromZero)))
                .ToList();

            return new AdminDashboardTopStationsResponse(range.From, range.To, totalTrips, stations,
                timeProvider.GetUtcNow().UtcDateTime);
        });

        return new(AdminDashboardResultStatus.Success, response);
    }

    public async Task<AdminDashboardResult<AdminDashboardBreakEvenResponse>> GetBreakEvenAsync(
        DashboardBreakEvenQuery query, CancellationToken cancellationToken = default)
    {
        var validation = await breakEvenValidator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return Invalid<AdminDashboardBreakEvenResponse>(validation.Errors);
        }

        var today = DashboardDateRules.Today(timeProvider);
        var firstDay = DashboardDateRules.ResolveMonth(query, today)!.Value;
        // Key kèm ngày hôm nay vì daysElapsed/projected đổi theo ngày.
        var key = string.Create(CultureInfo.InvariantCulture,
            $"admin-dashboard:break-even:{firstDay:yyyy-MM}:{today:yyyy-MM-dd}");
        var response = await GetOrCreateAsync(key, async () =>
        {
            var totals = await repository.GetPaidRevenueAsync(DashboardDateRules.StartOfDayUtc(firstDay),
                DashboardDateRules.StartOfDayUtc(firstDay.AddMonths(1)), cancellationToken);
            return BreakEvenCalculator.Calculate(firstDay, today, options.Value.BreakEvenMonthlyRevenue, totals,
                timeProvider.GetUtcNow().UtcDateTime);
        });

        return new(AdminDashboardResultStatus.Success, response);
    }

    // Không chủ động xoá cache: số liệu trễ tối đa CacheDuration (spec F2). Lỗi DB ném ra ngoài nên không bị cache.
    private async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory) where T : class
    {
        if (cache.TryGetValue(key, out T? cached) && cached is not null)
        {
            return cached;
        }

        var value = await factory();
        cache.Set(key, value, CacheDuration);
        return value;
    }

    // Key dùng ngày đã điền mặc định ⇒ gọi không tham số và gọi với đúng khoảng đó dùng chung cache.
    private static string CacheKey(string endpoint, DashboardDateRange range) =>
        string.Create(CultureInfo.InvariantCulture,
            $"admin-dashboard:{endpoint}:{range.From:yyyy-MM-dd}:{range.To:yyyy-MM-dd}");

    private static AdminDashboardResult<T> Invalid<T>(IEnumerable<ValidationFailure> failures) =>
        new(AdminDashboardResultStatus.InvalidQuery, ValidationErrors: failures
            .GroupBy(failure => failure.PropertyName)
            .ToDictionary(group => group.Key, group => group.Select(failure => failure.ErrorMessage).Distinct().ToArray()));
}
