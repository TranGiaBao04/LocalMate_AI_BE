using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class PaymentCreditRepository(AppDbContext db, TimeProvider clock) : IPaymentCreditRepository
{
    public async Task<IReadOnlyList<UpgradeSettlementSource>> LoadForSettlementAsync(Guid orderId,
        CancellationToken cancellationToken = default)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Credit settlement requires the shared locked transaction.");
        await db.Database.SqlQuery<Guid>($"SELECT \"PeriodId\" AS \"Value\" FROM \"PaymentOrderCredits\" WHERE \"OrderId\"={orderId} ORDER BY \"PeriodId\" FOR UPDATE")
            .ToListAsync(cancellationToken);
        await db.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM \"SubscriptionPeriods\" WHERE \"Id\" IN (SELECT \"PeriodId\" FROM \"PaymentOrderCredits\" WHERE \"OrderId\"={orderId}) ORDER BY \"Id\" FOR UPDATE")
            .ToListAsync(cancellationToken);
        var claims = await db.PaymentOrderCredits.Where(c => c.OrderId == orderId).OrderBy(c => c.PeriodId)
            .ToListAsync(cancellationToken);
        var ids = claims.Select(c => c.PeriodId).ToArray();
        var periods = await db.SubscriptionPeriods.Where(p => ids.Contains(p.Id)).OrderBy(p => p.Id)
            .ToListAsync(cancellationToken);
        foreach (var claim in claims) await db.Entry(claim).ReloadAsync(cancellationToken);
        foreach (var period in periods) await db.Entry(period).ReloadAsync(cancellationToken);
        return claims.Select(c => new UpgradeSettlementSource(c, periods.SingleOrDefault(p => p.Id == c.PeriodId))).ToArray();
    }
    public async Task AddClaimsAsync(IReadOnlyList<PaymentOrderCredit> claims, CancellationToken cancellationToken = default)
    {
        db.PaymentOrderCredits.AddRange(claims);
        await db.SaveChangesAsync(cancellationToken);
    }

    public async Task<UpgradeReleaseCandidate?> GetReleaseCandidateAsync(Guid userId, Guid orderId,
        CancellationToken cancellationToken = default)
    {
        var order = await db.PaymentOrders.AsNoTracking().SingleOrDefaultAsync(o => o.Id == orderId && o.UserId == userId
            && o.ProductKind == PaymentProductKind.SubscriptionPlan && o.Type == PaymentOrderType.Upgrade, cancellationToken);
        if (order is null) return null;
        var claims = await (from c in db.PaymentOrderCredits.AsNoTracking()
                            join p in db.SubscriptionPeriods.AsNoTracking() on c.PeriodId equals p.Id
                            where c.OrderId == orderId
                            orderby c.PeriodId
                            select new CreditClaimSnapshot(c.PeriodId, c.UserId, c.OriginalEndsAt, c.RemainingDays,
                                c.CalculatedCreditAmount, c.ReleasedAt, p.EndsAt, p.TerminatedAt, p.TerminatedByOrderId))
            .ToListAsync(cancellationToken);
        return new(order.Id, order.UserId, order.ProviderOrderCode, order.Status, order.Amount, order.CreditAmount,
            order.PaidAt, order.UpdatedAt, order.ExpiresAt, order.CheckoutUrl, order.QrCode, claims);
    }

    public async Task<UpgradeReleaseStatus> ReleaseAsync(UpgradeReleaseCandidate expected, CreditReleaseEvidence evidence,
        CancellationToken cancellationToken = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);
        if (await db.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM "Users" WHERE "Id"={expected.UserId} FOR UPDATE""")
            .SingleOrDefaultAsync(cancellationToken) != 1) return UpgradeReleaseStatus.NotFound;
        await db.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM "PaymentOrders" WHERE "Id"={expected.OrderId} FOR UPDATE""")
            .SingleOrDefaultAsync(cancellationToken);
        await db.Database.SqlQuery<Guid>($"""
            SELECT "PeriodId" AS "Value" FROM "PaymentOrderCredits" WHERE "OrderId"={expected.OrderId} ORDER BY "PeriodId" FOR UPDATE
            """).ToListAsync(cancellationToken);
        await db.Database.SqlQuery<Guid>($"""
            SELECT "Id" AS "Value" FROM "SubscriptionPeriods" WHERE "Id" IN
            (SELECT "PeriodId" FROM "PaymentOrderCredits" WHERE "OrderId"={expected.OrderId}) ORDER BY "Id" FOR UPDATE
            """).ToListAsync(cancellationToken);
        var current = await GetReleaseCandidateAsync(expected.UserId, expected.OrderId, cancellationToken);
        if (current is null) return UpgradeReleaseStatus.NotFound;
        if (current.Claims.Count > 0 && current.Claims.All(c => c.ReleasedAt is not null)) return UpgradeReleaseStatus.AlreadyReleased;
        var now = clock.GetUtcNow().UtcDateTime;
        if (current.Status is not (PaymentOrderStatus.Failed or PaymentOrderStatus.Expired)
            || current.PaidAt is not null || current.Status != expected.Status || current.UpdatedAt != expected.UpdatedAt
            || current.Amount != expected.Amount || current.CreditAmount != expected.CreditAmount
            || current.ProviderOrderCode != expected.ProviderOrderCode || current.ExpiresAt != expected.ExpiresAt
            || current.PaidAt != expected.PaidAt || !current.Claims.SequenceEqual(expected.Claims)
            || current.Claims.Count == 0 || current.Claims.Any(c => c.ReleasedAt is not null || c.UserId != current.UserId
                || c.SourceTerminatedAt is not null || c.SourceTerminatedByOrderId is not null || c.SourceEndsAt != c.OriginalEndsAt)
            || !evidence.IsValid(now) || evidence.RequestedAmount != current.Amount)
            return UpgradeReleaseStatus.StaleEvidence;
        var rows = await db.PaymentOrderCredits.Where(c => c.OrderId == current.OrderId).OrderBy(c => c.PeriodId)
            .ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            await db.Entry(row).ReloadAsync(cancellationToken);
            row.Release(now, evidence);
        }
        await db.SaveChangesAsync(cancellationToken);
        await tx.CommitAsync(cancellationToken);
        return UpgradeReleaseStatus.Released;
    }
}
