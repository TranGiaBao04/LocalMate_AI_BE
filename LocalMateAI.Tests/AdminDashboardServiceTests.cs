using LocalMateAI.Application.Dashboard;
using LocalMateAI.Application.DTOs.Dashboard;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.Dashboard;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Tests;

public sealed class AdminDashboardServiceTests
{
    // 12:00 ngày 01/10/2026 giờ VN.
    private static readonly DateTimeOffset Noon = new(2026, 10, 1, 5, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InvalidQuery_ReturnsErrorsWithoutQuerying()
    {
        var repository = new FakeRepository();

        var result = await CreateService(repository).GetSummaryAsync(new DashboardDateRangeQuery { From = "abc" });

        Assert.Equal(AdminDashboardResultStatus.InvalidQuery, result.Status);
        Assert.Contains("From", result.ValidationErrors!.Keys);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task ValidQuery_MapsCountsForResolvedRange()
    {
        var repository = new FakeRepository { Counts = new DashboardSummaryCounts(3, 7, 2, 78000m) };

        var result = await CreateService(repository).GetSummaryAsync(
            new DashboardDateRangeQuery { From = "2026-09-01", To = "2026-09-30" });

        Assert.Equal(AdminDashboardResultStatus.Success, result.Status);
        Assert.Equal(new AdminDashboardSummaryResponse(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30),
            3, 7, 2, 78000m, "VND", Noon.UtcDateTime), result.Response);
        Assert.Equal(new DateTime(2026, 8, 31, 17, 0, 0, DateTimeKind.Utc), repository.LastStartUtc);
        Assert.Equal(new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc), repository.LastEndUtc);
    }

    [Fact]
    public async Task SameRange_IsServedFromCache_IncludingDefaultRange()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);

        await service.GetSummaryAsync(new DashboardDateRangeQuery());
        await service.GetSummaryAsync(new DashboardDateRangeQuery { From = "2026-09-02", To = "2026-10-01" });

        Assert.Equal(1, repository.Calls);
    }

    [Fact]
    public async Task DifferentRanges_UseSeparateCacheEntries()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);

        await service.GetSummaryAsync(new DashboardDateRangeQuery { From = "2026-09-01", To = "2026-09-30" });
        await service.GetSummaryAsync(new DashboardDateRangeQuery { From = "2026-09-01", To = "2026-09-29" });

        Assert.Equal(2, repository.Calls);
    }

    [Fact]
    public async Task RepositoryFailure_IsNotCached()
    {
        var repository = new FakeRepository { FailNext = true };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetSummaryAsync(new DashboardDateRangeQuery()));
        var result = await service.GetSummaryAsync(new DashboardDateRangeQuery());

        Assert.Equal(AdminDashboardResultStatus.Success, result.Status);
        Assert.Equal(2, repository.Calls);
    }

    [Fact]
    public async Task RevenueDaily_FillsMissingDaysWithZero_InAscendingOrder()
    {
        var repository = new FakeRepository
        {
            DailyRows =
            [
                new DailyRevenueRow(new DateOnly(2026, 9, 28), 1, 19000m),
                new DailyRevenueRow(new DateOnly(2026, 9, 30), 2, 78000m)
            ]
        };
        var service = CreateService(repository);
        var query = new DashboardDateRangeQuery { From = "2026-09-27", To = "2026-10-01" };

        var result = await service.GetRevenueDailyAsync(query);
        await service.GetRevenueDailyAsync(query);

        var response = result.Response!;
        Assert.Equal(
        [
            new DailyRevenueResponse(new DateOnly(2026, 9, 27), 0m, 0),
            new DailyRevenueResponse(new DateOnly(2026, 9, 28), 19000m, 1),
            new DailyRevenueResponse(new DateOnly(2026, 9, 29), 0m, 0),
            new DailyRevenueResponse(new DateOnly(2026, 9, 30), 78000m, 2),
            new DailyRevenueResponse(new DateOnly(2026, 10, 1), 0m, 0)
        ], response.Days);
        Assert.Equal(97000m, response.TotalRevenue);
        Assert.Equal("VND", response.Currency);
        Assert.Equal(1, repository.Calls);
    }

    [Fact]
    public async Task RevenueDaily_InvalidQuery_DoesNotQuery()
    {
        var repository = new FakeRepository();

        var result = await CreateService(repository).GetRevenueDailyAsync(new DashboardDateRangeQuery { To = "2030-01-01" });

        Assert.Equal(AdminDashboardResultStatus.InvalidQuery, result.Status);
        Assert.Contains("To", result.ValidationErrors!.Keys);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task BreakEven_QueriesVietnamMonthWindow_AndUsesConfiguredTarget()
    {
        var repository = new FakeRepository { Totals = new PaidRevenueTotals(5, 177000m) };

        var result = await CreateService(repository, target: 5_000_000m).GetBreakEvenAsync(new DashboardBreakEvenQuery());

        Assert.Equal(new DateTime(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc), repository.LastStartUtc);
        Assert.Equal(new DateTime(2026, 10, 31, 17, 0, 0, DateTimeKind.Utc), repository.LastEndUtc);
        var response = result.Response!;
        Assert.Equal("2026-10", response.Month);
        Assert.Equal(5_000_000m, response.Target);
        Assert.Equal(177000m, response.Revenue);
        Assert.Equal(5, response.PaidOrders);
    }

    [Fact]
    public async Task BreakEven_InvalidMonth_DoesNotQuery()
    {
        var repository = new FakeRepository();

        var result = await CreateService(repository).GetBreakEvenAsync(new DashboardBreakEvenQuery { Month = "2026-11" });

        Assert.Equal(AdminDashboardResultStatus.InvalidQuery, result.Status);
        Assert.Contains("Month", result.ValidationErrors!.Keys);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task TopStations_SortsByTripCountThenOrder_AndLimitsAfterTotal()
    {
        var repository = new FakeRepository
        {
            StationRows =
            [
                new StationTripCountRow(3, "Ba Son", 1),
                new StationTripCountRow(2, "Nhà hát Thành phố", 1),
                new StationTripCountRow(1, "Bến Thành", 1)
            ]
        };

        var result = await CreateService(repository).GetTopStationsAsync(new DashboardTopStationsQuery { Limit = 2 });

        var response = result.Response!;
        Assert.Equal(3, response.TotalTrips);
        Assert.Equal(
        [
            new TopStationResponse(1, "Bến Thành", 1, 33.3m),
            new TopStationResponse(2, "Nhà hát Thành phố", 1, 33.3m)
        ], response.Stations);
    }

    [Fact]
    public async Task TopStations_HigherCountComesFirst()
    {
        var repository = new FakeRepository
        {
            StationRows = [new StationTripCountRow(1, "Bến Thành", 1), new StationTripCountRow(3, "Ba Son", 3)]
        };

        var result = await CreateService(repository).GetTopStationsAsync(new DashboardTopStationsQuery());

        Assert.Equal([3, 1], result.Response!.Stations.Select(station => station.Order));
        Assert.Equal(75.0m, result.Response.Stations[0].SharePercent);
    }

    [Fact]
    public async Task TopStations_NoTrips_ReturnsEmptyWithoutDividingByZero()
    {
        var result = await CreateService(new FakeRepository()).GetTopStationsAsync(new DashboardTopStationsQuery());

        Assert.Equal(0, result.Response!.TotalTrips);
        Assert.Empty(result.Response.Stations);
    }

    [Fact]
    public async Task TopStations_CachesPerLimit()
    {
        var repository = new FakeRepository();
        var service = CreateService(repository);

        await service.GetTopStationsAsync(new DashboardTopStationsQuery { Limit = 5 });
        await service.GetTopStationsAsync(new DashboardTopStationsQuery { Limit = 5 });
        await service.GetTopStationsAsync(new DashboardTopStationsQuery { Limit = 3 });

        Assert.Equal(2, repository.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(15)]
    [InlineData(-1)]
    public async Task TopStations_InvalidLimit_DoesNotQuery(int limit)
    {
        var repository = new FakeRepository();

        var result = await CreateService(repository).GetTopStationsAsync(new DashboardTopStationsQuery { Limit = limit });

        Assert.Equal(AdminDashboardResultStatus.InvalidQuery, result.Status);
        Assert.Equal(["Limit"], result.ValidationErrors!.Keys);
        Assert.Equal(0, repository.Calls);
    }

    [Fact]
    public async Task TopStations_StillValidatesDateRange()
    {
        var result = await CreateService(new FakeRepository()).GetTopStationsAsync(
            new DashboardTopStationsQuery { To = "2026-10-02", Limit = 14 });

        Assert.Equal(["To"], result.ValidationErrors!.Keys);
    }

    private static AdminDashboardService CreateService(FakeRepository repository, decimal target = 5_000_000m)
    {
        var clock = new FixedTimeProvider(Noon);
        return new AdminDashboardService(repository,
            new DashboardDateRangeQueryValidator(clock),
            new DashboardTopStationsQueryValidator(clock),
            new DashboardBreakEvenQueryValidator(clock),
            Options.Create(new DashboardOptions { BreakEvenMonthlyRevenue = target }),
            new MemoryCache(new MemoryCacheOptions()),
            clock);
    }

    private sealed class FakeRepository : IAdminDashboardRepository
    {
        public DashboardSummaryCounts Counts { get; init; } = new(0, 0, 0, 0m);
        public PaidRevenueTotals Totals { get; init; } = new(0, 0m);
        public IReadOnlyList<DailyRevenueRow> DailyRows { get; init; } = [];
        public IReadOnlyList<StationTripCountRow> StationRows { get; init; } = [];
        public bool FailNext { get; set; }
        public int Calls { get; private set; }
        public DateTime LastStartUtc { get; private set; }
        public DateTime LastEndUtc { get; private set; }

        public Task<DashboardSummaryCounts> GetSummaryAsync(DateTime startUtc, DateTime endUtc,
            CancellationToken cancellationToken = default) => Record(startUtc, endUtc, Counts);

        public Task<PaidRevenueTotals> GetPaidRevenueAsync(DateTime startUtc, DateTime endUtc,
            CancellationToken cancellationToken = default) => Record(startUtc, endUtc, Totals);

        public Task<IReadOnlyList<DailyRevenueRow>> GetDailyRevenueAsync(DateTime startUtc, DateTime endUtc,
            CancellationToken cancellationToken = default) => Record(startUtc, endUtc, DailyRows);

        public Task<IReadOnlyList<StationTripCountRow>> GetTripCountsByStationAsync(DateTime startUtc, DateTime endUtc,
            CancellationToken cancellationToken = default) => Record(startUtc, endUtc, StationRows);

        private Task<T> Record<T>(DateTime startUtc, DateTime endUtc, T value)
        {
            Calls++;
            LastStartUtc = startUtc;
            LastEndUtc = endUtc;
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("Database unavailable.");
            }

            return Task.FromResult(value);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
