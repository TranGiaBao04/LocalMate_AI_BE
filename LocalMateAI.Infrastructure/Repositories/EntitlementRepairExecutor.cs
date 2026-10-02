using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class EntitlementRepairExecutor(AppDbContext context, TimeProvider clock) : IEntitlementRepairExecutor
{
    public async Task<EntitlementRepairResponse?> ExecuteAsync(Guid orderId, Guid actorId, string reason, CancellationToken cancellationToken = default)
    {
        if (!EntitlementRepairReason.TryNormalize(reason, out var normalized) || actorId == Guid.Empty)
            throw new ArgumentException("A valid actor and repair reason are required.");
        var owner = await context.PaymentOrders.AsNoTracking().Where(o => o.Id == orderId).Select(o => (Guid?)o.UserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (owner is null) return null;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        var userLocked = await context.Database.SqlQuery<int>(
            $"""SELECT 1 AS "Value" FROM "Users" WHERE "Id" = {owner.Value} FOR UPDATE""")
            .SingleOrDefaultAsync(cancellationToken);
        if (userLocked != 1) return null;
        var orderLocked = await context.Database.SqlQuery<int>(
            $"""SELECT 1 AS "Value" FROM "PaymentOrders" WHERE "Id" = {orderId} FOR UPDATE""")
            .SingleOrDefaultAsync(cancellationToken);
        if (orderLocked != 1) return null;
        var order = await context.PaymentOrders.SingleAsync(o => o.Id == orderId, cancellationToken);
        await context.Entry(order).ReloadAsync(cancellationToken);
        if (order.UserId != owner.Value) throw new InvalidOperationException("Order ownership changed during repair.");
        if (order.ProductKind == PaymentProductKind.SubscriptionPlan && order.Type == PaymentOrderType.Upgrade)
        {
            await context.Database.SqlQuery<Guid>($"SELECT \"PeriodId\" AS \"Value\" FROM \"PaymentOrderCredits\" WHERE \"OrderId\"={orderId} ORDER BY \"PeriodId\" FOR UPDATE")
                .ToListAsync(cancellationToken);
            await context.Database.SqlQuery<Guid>($"SELECT \"Id\" AS \"Value\" FROM \"SubscriptionPeriods\" WHERE \"Id\" IN (SELECT \"PeriodId\" FROM \"PaymentOrderCredits\" WHERE \"OrderId\"={orderId}) ORDER BY \"Id\" FOR UPDATE")
                .ToListAsync(cancellationToken);
        }
        var before = context.Entry(order).CurrentValues.Clone();
        var evidence = (await EntitlementRepairEvidenceReader.ReadAsync(context, orderId, cancellationToken))!;
        var now = clock.GetUtcNow().UtcDateTime;
        var assessment = EntitlementRepairAssessmentPolicy.Assess(evidence, now);
        var periodId = assessment.Entitlement.SubscriptionPeriodId;
        var startsAt = assessment.Entitlement.StartsAt;
        var endsAt = assessment.Entitlement.EndsAt;
        var outcome = assessment.Eligibility.Eligible ? EntitlementRepairOutcome.Repaired
            : assessment.Entitlement.GrantStatus == "Granted" ? EntitlementRepairOutcome.AlreadyGranted
            : assessment.IsConflict ? EntitlementRepairOutcome.Conflict : EntitlementRepairOutcome.NotEligible;
        if (outcome == EntitlementRepairOutcome.Repaired)
        {
            var period = new SubscriptionPeriod
            {
                UserId = order.UserId, PlanId = order.PlanId!.Value, PlanVersionId = order.PlanVersionId!.Value,
                SourcePaymentOrderId = order.Id,
                StartsAt = assessment.Eligibility.ProposedStartsAt!.Value,
                EndsAt = assessment.Eligibility.ProposedEndsAt!.Value
            };
            context.SubscriptionPeriods.Add(period);
            periodId = period.Id; startsAt = period.StartsAt; endsAt = period.EndsAt;
        }
        var audit = new EntitlementRepairAudit
        {
            PaymentOrderId = order.Id, SubscriptionPeriodId = periodId, ActorUserId = actorId,
            Reason = normalized, Outcome = outcome, DecisionCode = assessment.Eligibility.Code,
            ReconstructionMode = assessment.Eligibility.ReconstructionMode, OccurredAt = now
        };
        context.EntitlementRepairAudits.Add(audit);
        context.ChangeTracker.DetectChanges();
        if (context.Entry(order).Properties.Any(p => !Equals(before[p.Metadata.Name], p.CurrentValue)))
            throw new InvalidOperationException("Repair must not mutate the payment order.");
        await context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new(outcome, order.Id, periodId, startsAt, endsAt, audit.Id, audit.DecisionCode, audit.ReconstructionMode);
    }
}
