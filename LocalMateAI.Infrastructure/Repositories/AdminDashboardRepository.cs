using LocalMateAI.Application.DTOs.Dashboard;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class AdminDashboardRepository(AppDbContext context) : IAdminDashboardRepository
{
    public async Task<DashboardSummaryCounts> GetSummaryAsync(DateTime startUtc, DateTime endUtc,
        CancellationToken cancellationToken = default)
    {
        // DbContext không chạy song song được nên các truy vấn chạy lần lượt.
        var newUsers = await context.Users.AsNoTracking()
            .LongCountAsync(user => user.CreatedAt >= startUtc && user.CreatedAt < endUtc, cancellationToken);

        // Đếm cả trip đã xoá mềm: đây là số trip đã được tạo trong khoảng, không phải số trip còn lại.
        var tripsCreated = await context.Trips.AsNoTracking()
            .LongCountAsync(trip => trip.CreatedAt >= startUtc && trip.CreatedAt < endUtc, cancellationToken);

        var payments = await GetPaidRevenueAsync(startUtc, endUtc, cancellationToken);

        return new DashboardSummaryCounts(newUsers, tripsCreated, payments.PaidOrders, payments.Revenue);
    }

    public async Task<PaidRevenueTotals> GetPaidRevenueAsync(DateTime startUtc, DateTime endUtc,
        CancellationToken cancellationToken = default)
    {
        var totals = await PaidOrdersBetween(startUtc, endUtc)
            .GroupBy(_ => 1)
            .Select(group => new { Count = group.LongCount(), Revenue = group.Sum(order => order.Amount) })
            .SingleOrDefaultAsync(cancellationToken);

        return new PaidRevenueTotals(totals?.Count ?? 0, totals?.Revenue ?? 0m);
    }

    public async Task<IReadOnlyList<DailyRevenueRow>> GetDailyRevenueAsync(DateTime startUtc, DateTime endUtc,
        CancellationToken cancellationToken = default)
    {
        // Gom theo ngày giờ VN bằng SQL. Không dùng LINQ .Date vì date_trunc trên timestamptz phụ thuộc
        // múi giờ của phiên kết nối.
        var rows = await context.Database.SqlQuery<DailyRevenueSqlRow>($"""
            SELECT ("PaidAt" AT TIME ZONE 'Asia/Ho_Chi_Minh')::date AS "Date",
                   count(*) AS "PaidOrders",
                   sum("Amount") AS "Revenue"
            FROM "PaymentOrders"
            WHERE "Status" = 'Paid' AND "PaidAt" >= {startUtc} AND "PaidAt" < {endUtc}
            GROUP BY 1
            ORDER BY 1
            """).ToListAsync(cancellationToken);

        return rows.Select(row => new DailyRevenueRow(row.Date, row.PaidOrders, row.Revenue)).ToList();
    }

    public async Task<IReadOnlyList<StationTripCountRow>> GetTripCountsByStationAsync(DateTime startUtc,
        DateTime endUtc, CancellationToken cancellationToken = default)
    {
        // Ga của trip = ga gần điểm xuất phát nhất, cùng cách tính với stationName của trip detail
        // (TripRepository) để hai màn không lệch nhau. Đếm cả trip đã xoá mềm.
        var rows = await context.Database.SqlQuery<StationTripCountSqlRow>($"""
            SELECT s."Order" AS "StationOrder", s."Name" AS "StationName", count(*) AS "TripCount"
            FROM "Trips" t
            CROSS JOIN LATERAL (
                SELECT ms."Order", ms."Name"
                FROM "MetroStations" ms
                ORDER BY ST_Distance(
                    ST_SetSRID(ST_MakePoint(t."StartLongitude", t."StartLatitude"), 4326)::geography,
                    ms."Location"::geography), ms."Order"
                LIMIT 1
            ) s
            WHERE t."CreatedAt" >= {startUtc} AND t."CreatedAt" < {endUtc}
            GROUP BY s."Order", s."Name"
            """).ToListAsync(cancellationToken);

        return rows.Select(row => new StationTripCountRow(row.StationOrder, row.StationName, row.TripCount)).ToList();
    }

    // Doanh thu tính theo ngày tiền về (PaidAt), khác transactions/summary (lọc theo CreatedAt của đơn).
    // Status = Paid là hằng số trong SQL nên Postgres dùng được index lọc IX_PaymentOrders_PaidAt_Paid.
    private IQueryable<PaymentOrder> PaidOrdersBetween(DateTime startUtc, DateTime endUtc) =>
        context.PaymentOrders.AsNoTracking()
            .Where(order => order.Status == PaymentOrderStatus.Paid
                && order.PaidAt >= startUtc && order.PaidAt < endUtc);

    private sealed class DailyRevenueSqlRow
    {
        public DateOnly Date { get; init; }
        public long PaidOrders { get; init; }
        public decimal Revenue { get; init; }
    }

    private sealed class StationTripCountSqlRow
    {
        public int StationOrder { get; init; }
        public string StationName { get; init; } = "";
        public long TripCount { get; init; }
    }
}
