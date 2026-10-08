using LocalMateAI.Application.DTOs.Dashboard;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

public sealed class AdminDashboardRepositoryPostgresTests
{
    // 00:00 VN ngày 01/10/2026 và 00:00 VN ngày 02/10/2026.
    private static readonly DateTime DayStart = new(2026, 9, 30, 17, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime NextDayStart = DayStart.AddDays(1);

    [Fact]
    public async Task Summary_CountsOnlyRowsInsideVietnamDay_IncludingSoftDeletedTrips()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var role = await TestRoles.GetUserRoleIdAsync(c);

        var insideUser = new User { FullName = "Inside", Email = "inside@dashboard.test", RoleId = role };
        var beforeUser = new User { FullName = "Before", Email = "before@dashboard.test", RoleId = role };
        var afterUser = new User { FullName = "After", Email = "after@dashboard.test", RoleId = role };
        c.Users.AddRange(insideUser, beforeUser, afterUser);

        var activeTrip = new Trip { UserId = insideUser.Id };
        var deletedTrip = new Trip { UserId = insideUser.Id, DeletedAt = DayStart.AddHours(3) };
        var outsideTrip = new Trip { UserId = insideUser.Id };
        c.Trips.AddRange(activeTrip, deletedTrip, outsideTrip);

        var paidInside = PaidOrder(insideUser.Id, 900201, PlanCode.TripPass, 19000m, DayStart.AddHours(10));
        var paidMembership = PaidOrder(insideUser.Id, 900202, PlanCode.Membership, 59000m, NextDayStart.AddSeconds(-1));
        var paidOutside = PaidOrder(insideUser.Id, 900203, PlanCode.TripPass, 19000m, NextDayStart);
        var pending = PaidOrder(insideUser.Id, 900204, PlanCode.TripPass, 19000m, null);
        pending.Status = PaymentOrderStatus.Pending;
        c.PaymentOrders.AddRange(paidInside, paidMembership, paidOutside, pending);
        await c.SaveChangesAsync();

        // SaveChanges tự ghi CreatedAt = giờ hiện tại ⇒ đặt lại bằng SQL.
        await SetCreatedAtAsync(c, "Users", insideUser.Id, DayStart);
        await SetCreatedAtAsync(c, "Users", beforeUser.Id, DayStart.AddSeconds(-1));
        await SetCreatedAtAsync(c, "Users", afterUser.Id, NextDayStart);
        await SetCreatedAtAsync(c, "Trips", activeTrip.Id, DayStart.AddHours(1));
        await SetCreatedAtAsync(c, "Trips", deletedTrip.Id, DayStart.AddHours(2));
        await SetCreatedAtAsync(c, "Trips", outsideTrip.Id, DayStart.AddDays(-1));
        // Đơn tạo hôm trước nhưng trả trong ngày vẫn tính (theo PaidAt).
        await SetCreatedAtAsync(c, "PaymentOrders", paidInside.Id, DayStart.AddDays(-1));
        c.ChangeTracker.Clear();

        var range = new DashboardDateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 1));
        var summary = await new AdminDashboardRepository(c).GetSummaryAsync(range.StartUtc, range.EndUtc);

        Assert.Equal(new DashboardSummaryCounts(NewUsers: 1, TripsCreated: 2, PaidOrders: 2, Revenue: 78000m), summary);
        Assert.Empty(c.ChangeTracker.Entries());
        Assert.False(c.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task Summary_EmptyRange_ReturnsZeros()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();

        var summary = await new AdminDashboardRepository(c).GetSummaryAsync(
            DashboardDateRules.StartOfDayUtc(new DateOnly(2001, 1, 1)),
            DashboardDateRules.StartOfDayUtc(new DateOnly(2001, 1, 2)));

        Assert.Equal(new DashboardSummaryCounts(0, 0, 0, 0m), summary);
    }

    [Fact]
    public async Task DailyRevenue_GroupsByVietnamDate_AndSkipsUnpaidOrders()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var role = await TestRoles.GetUserRoleIdAsync(c);
        var user = new User { FullName = "Daily", Email = "daily@dashboard.test", RoleId = role };
        c.Users.Add(user);
        // 23:30 VN ngày 01/10 (16:30Z) ⇒ ngày 01/10; 00:30 VN ngày 02/10 (17:30Z) ⇒ ngày 02/10.
        var lateEvening = PaidOrder(user.Id, 900301, PlanCode.TripPass, 19000m, DayStart.AddHours(23.5));
        var morning = PaidOrder(user.Id, 900302, PlanCode.TripPass, 19000m, DayStart.AddHours(7));
        var afterMidnight = PaidOrder(user.Id, 900303, PlanCode.Membership, 59000m, NextDayStart.AddMinutes(30));
        var pending = PaidOrder(user.Id, 900304, PlanCode.TripPass, 19000m, null);
        pending.Status = PaymentOrderStatus.Pending;
        c.PaymentOrders.AddRange(lateEvening, morning, afterMidnight, pending);
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();
        var repository = new AdminDashboardRepository(c);
        var range = new DashboardDateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2));

        var rows = await repository.GetDailyRevenueAsync(range.StartUtc, range.EndUtc);
        var totals = await repository.GetPaidRevenueAsync(range.StartUtc, range.EndUtc);

        Assert.Equal(
        [
            new DailyRevenueRow(new DateOnly(2026, 10, 1), 2, 38000m),
            new DailyRevenueRow(new DateOnly(2026, 10, 2), 1, 59000m)
        ], rows);
        Assert.Equal(new PaidRevenueTotals(3, 97000m), totals);
    }

    [Fact]
    public async Task DailyFinalizedTrips_GroupsByVietnamFinalizeDate_AndMatchesPublicTotal()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var role = await TestRoles.GetUserRoleIdAsync(c);
        var user = new User { FullName = "Finalized", Email = "finalized@dashboard.test", RoleId = role };
        c.Users.Add(user);

        // 23:30 VN ngày 01/10 ⇒ ngày 01/10; 00:30 VN ngày 02/10 ⇒ ngày 02/10.
        var lateEvening = FinalizedTrip(user.Id, DayStart.AddHours(23.5));
        var afterMidnight = FinalizedTrip(user.Id, NextDayStart.AddMinutes(30));
        var deleted = FinalizedTrip(user.Id, DayStart.AddHours(1));
        deleted.DeletedAt = DayStart.AddHours(2);
        // Trip chốt trước khi có cột FinalizedAt: tính theo CreatedAt.
        var legacy = FinalizedTrip(user.Id, null);
        var draft = new Trip { UserId = user.Id };
        var beforeRange = FinalizedTrip(user.Id, DayStart.AddSeconds(-1));
        c.Trips.AddRange(lateEvening, afterMidnight, deleted, legacy, draft, beforeRange);
        await c.SaveChangesAsync();

        // SaveChanges tự ghi CreatedAt = giờ hiện tại ⇒ đặt lại bằng SQL.
        await SetCreatedAtAsync(c, "Trips", legacy.Id, NextDayStart.AddHours(2));
        await SetCreatedAtAsync(c, "Trips", draft.Id, DayStart.AddHours(3));
        // Tạo từ hôm trước nhưng chốt trong ngày vẫn tính theo ngày chốt.
        await SetCreatedAtAsync(c, "Trips", lateEvening.Id, DayStart.AddDays(-3));
        await SetCreatedAtAsync(c, "Trips", afterMidnight.Id, DayStart.AddDays(-3));
        await SetCreatedAtAsync(c, "Trips", deleted.Id, DayStart.AddDays(-3));
        await SetCreatedAtAsync(c, "Trips", beforeRange.Id, DayStart.AddDays(-3));
        c.ChangeTracker.Clear();
        var range = new DashboardDateRange(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2));

        var rows = await new AdminDashboardRepository(c).GetDailyFinalizedTripsAsync(range.StartUtc, range.EndUtc);
        var total = await new PublicStatsRepository(c).CountFinalizedTripsAsync();

        Assert.Equal(
        [
            new DailyTripsFinalizedResponse(new DateOnly(2026, 10, 1), 2),
            new DailyTripsFinalizedResponse(new DateOnly(2026, 10, 2), 2)
        ], rows);
        Assert.Equal(5, total);
    }

    private static Trip FinalizedTrip(Guid userId, DateTime? finalizedAt) => new()
    {
        UserId = userId,
        Status = TripStatus.Finalized,
        FinalizedAt = finalizedAt
    };

    // Đơn legacy: trigger không bắt đơn Paid phải có kỳ subscription đi kèm (đơn Native thì bắt), đủ cho đếm doanh thu.
    private static PaymentOrder PaidOrder(Guid userId, long code, PlanCode plan, decimal amount, DateTime? paidAt) => new()
    {
        UserId = userId,
        ProviderOrderCode = code,
        PlanCode = plan,
        PlanVersionBinding = PlanVersionBinding.LegacyUnresolved,
        Type = PaymentOrderType.Purchase,
        Status = PaymentOrderStatus.Paid,
        Amount = amount,
        ExpiresAt = DayStart.AddDays(2),
        PaidAt = paidAt
    };

    private static Task<int> SetCreatedAtAsync(AppDbContext c, string table, Guid id, DateTime createdAt) =>
        c.Database.ExecuteSqlRawAsync($"UPDATE \"{table}\" SET \"CreatedAt\" = {{0}} WHERE \"Id\" = {{1}}", createdAt, id);
}
