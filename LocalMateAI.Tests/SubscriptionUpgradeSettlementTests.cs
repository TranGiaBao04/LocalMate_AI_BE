using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Domain.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMateAI.Tests;

public sealed class SubscriptionUpgradeSettlementTests
{
    private static readonly DateTime Now = SubscriptionCheckoutQuoteTests.Now;
    private static readonly Guid User = SubscriptionCheckoutQuoteTests.UserId;

    private sealed class Executor(PaymentOrder order) : IPaymentSettlementExecutor
    {
        public Task<PaymentSettlementExecution<T>> ExecuteAsync<T>(long code, PaymentTransitionContext context,
            Func<PaymentOrder, CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
            ExecuteAsync(code, operation, ct);
        public async Task<PaymentSettlementExecution<T>> ExecuteAsync<T>(long code,
            Func<PaymentOrder, CancellationToken, Task<T>> operation, CancellationToken ct = default) =>
            new(true, await operation(order, ct));
    }

    private static async Task<(PaymentServiceTests.Fixture Fixture, PaymentOrder Order, PaymentSettlementService Service)> Prepared()
    {
        var f = new PaymentServiceTests.Fixture(now: Now);
        SubscriptionCheckoutQuoteTests.AddNative(f);
        var checkout = await f.Service.CheckoutAsync(User, "Membership");
        Assert.Equal(PaymentIntentResultStatus.Success, checkout.Status);
        var o = Assert.Single(f.Orders.Items);
        // These collaborators must never be touched by S4 Upgrade receipt handling.
        var service = new PaymentSettlementService(new Executor(o), f.Subscriptions, null!, null!,
            new PlanVersionFoundationPostgresTests.Clock(Now), NullLogger<PaymentSettlementService>.Instance,
            creditRepository: f.Credits);
        return (f, o, service);
    }

    [Fact]
    public async Task SuccessUsesPinnedPriceAndOneTimestamp_FullTerm_NoReceipt_NoRecalculation()
    {
        var (f, o, service) = await Prepared();
        Assert.NotEqual(o.CreditAmount, f.Credits.Rows.Sum(c => c.CalculatedCreditAmount));
        var source = Assert.Single(f.Subscriptions.Periods);
        var rawEnd = source.EndsAt;
        var bound = f.Subscriptions.Versions.Single(v => v.Id == o.PlanVersionId);
        await f.Subscriptions.PublishVersionAsync(new() { PlanId = bound.PlanId, VersionNumber = 2,
            Price = 99000, DurationDays = 45 });
        var result = await service.ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true));
        Assert.Equal(PaymentSettlementStatus.Settled, result.Status);
        Assert.Equal(Now, result.OccurredAt); Assert.Equal(Now, o.PaidAt);
        Assert.Equal(Now, source.TerminatedAt); Assert.Equal(o.Id, source.TerminatedByOrderId);
        Assert.Equal(rawEnd, source.EndsAt);
        var target = Assert.Single(f.Subscriptions.Periods, p => p.SourcePaymentOrderId == o.Id);
        Assert.Equal(Now, target.StartsAt); Assert.Equal(Now.AddDays(30), target.EndsAt);
        Assert.Equal(bound.Id, target.PlanVersionId);
        Assert.All(f.Credits.Rows, c => Assert.Null(c.ReleasedAt));
        Assert.Equal(PaymentSettlementStatus.AlreadyPaid,
            (await service.ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true))).Status);
        Assert.Equal(2, f.Subscriptions.Periods.Count);
    }

    [Fact]
    public async Task ZeroCreditFutureSourceIsTerminatedWithoutRewritingOriginalInterval()
    {
        var (f, o, service) = await Prepared();
        var (_, future) = SubscriptionCheckoutQuoteTests.AddNative(f, Now.AddDays(5));
        f.Credits.Rows.Add(new() { OrderId = o.Id, UserId = User, PeriodId = future.Id,
            OriginalEndsAt = future.EndsAt, CalculatedCreditAmount = 0 });
        var start = future.StartsAt; var end = future.EndsAt;
        Assert.Equal(PaymentSettlementStatus.Settled,
            (await service.ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true))).Status);
        Assert.Equal(start, future.StartsAt); Assert.Equal(end, future.EndsAt);
        Assert.Equal(Now, future.TerminatedAt);
        Assert.False(SubscriptionPeriodLifecycle.HasEffectiveDuration(future));
    }

    [Theory]
    [InlineData("missing_claim")]
    [InlineData("released")]
    [InlineData("mixed")]
    [InlineData("missing_period")]
    [InlineData("owner")]
    [InlineData("snapshot")]
    [InlineData("terminated")]
    [InlineData("target")]
    public async Task CreditConflictIsDurable_Idempotent_AndNeverGrants(string flaw)
    {
        var (f, o, service) = await Prepared();
        var source = Assert.Single(f.Subscriptions.Periods);
        var claim = Assert.Single(f.Credits.Rows);
        switch (flaw)
        {
            case "missing_claim": f.Credits.Rows.Clear(); break;
            case "released": claim.ReleasedAt = Now; break;
            case "mixed":
                var (_, future) = SubscriptionCheckoutQuoteTests.AddNative(f, Now.AddDays(5));
                f.Credits.Rows.Add(new() { OrderId = o.Id, UserId = User, PeriodId = future.Id,
                    OriginalEndsAt = future.EndsAt, ReleasedAt = Now }); break;
            case "missing_period": f.Subscriptions.Periods.Clear(); break;
            case "owner": f.Credits.Rows[0] = new() { OrderId = o.Id, PeriodId = source.Id,
                    UserId = Guid.NewGuid(), OriginalEndsAt = source.EndsAt }; break;
            case "snapshot": f.Credits.Rows[0] = new() { OrderId = o.Id, PeriodId = source.Id,
                    UserId = User, OriginalEndsAt = source.EndsAt.AddSeconds(1) }; break;
            case "terminated": source.Terminate(Now.AddSeconds(-1), Guid.NewGuid()); break;
            case "target": SubscriptionCheckoutQuoteTests.AddNative(f, Now.AddDays(1), PlanCode.Membership); break;
        }
        var before = f.Subscriptions.Periods.Count;
        var result = await service.ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true));
        Assert.Equal(PaymentSettlementStatus.CreditConflict, result.Status);
        Assert.Equal("upgrade_credit_conflict", result.TransitionReasonCode);
        Assert.Equal(PaymentOrderStatus.ReviewRequired, o.Status); Assert.Null(o.PaidAt);
        Assert.Equal(before, f.Subscriptions.Periods.Count);
        Assert.Equal(PaymentSettlementStatus.CreditConflict,
            (await service.ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, false))).Status);
        Assert.Equal(PaymentOrderStatus.ReviewRequired, o.Status);
        Assert.Equal(PaymentIntentResultStatus.PaymentReviewRequired,
            (await f.Service.CheckoutAsync(User, "Membership")).Status);
    }

    [Theory]
    [InlineData("binding")]
    [InlineData("no_plan")]
    [InlineData("no_version")]
    [InlineData("financial")]
    [InlineData("negative_credit")]
    [InlineData("zero_amount")]
    [InlineData("free")]
    [InlineData("wrong_version_owner")]
    [InlineData("duration")]
    [InlineData("price")]
    public async Task InvalidPurchasedContractNeverTerminates(string flaw)
    {
        var (f, o, service) = await Prepared();
        switch (flaw)
        {
            case "binding": o.PlanVersionBinding = PlanVersionBinding.LegacyVerified; break;
            case "no_plan": o.PlanId = null; break;
            case "no_version": o.PlanVersionId = null; break;
            case "financial": o.CreditAmount++; break;
            case "negative_credit": o.CreditAmount = -1; break;
            case "zero_amount": o.Amount = 0; break;
            case "free":
                var free = f.Subscriptions.Plans.Single(p => p.Code == "FREE");
                o.PlanId = free.Id; o.PlanVersionId = free.CurrentVersionId; break;
            case "wrong_version_owner": o.PlanVersionId = f.Subscriptions.Versions.Single(v => v.PlanId != o.PlanId && v.Price > 0).Id; break;
            case "duration":
            case "price":
                var old = f.Subscriptions.Versions.Single(v => v.Id == o.PlanVersionId);
                f.Subscriptions.Versions.Remove(old);
                f.Subscriptions.Versions.Add(new() { Id = old.Id, PlanId = old.PlanId,
                    Price = flaw == "price" ? 0 : old.Price, DurationDays = flaw == "duration" ? 0 : old.DurationDays }); break;
        }
        Assert.Equal(PaymentSettlementStatus.InvalidPaidPlan,
            (await service.ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true))).Status);
        Assert.All(f.Subscriptions.Periods, p => Assert.Null(p.TerminatedAt));
        Assert.Null(o.PaidAt);
    }

    [Theory]
    [InlineData(false, false, PaymentSettlementStatus.NonSuccessful)]
    [InlineData(true, true, PaymentSettlementStatus.AmountMismatch)]
    public async Task NonSuccessAndMismatchRetainReservations(bool success, bool mismatch, PaymentSettlementStatus expected)
    {
        var (f, o, service) = await Prepared();
        Assert.Equal(expected, (await service.ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode,
            o.Amount - (mismatch ? 1 : 0), success))).Status);
        Assert.Equal(PaymentOrderStatus.Failed, o.Status);
        Assert.All(f.Subscriptions.Periods, p => Assert.Null(p.TerminatedAt));
        Assert.All(f.Credits.Rows, c => Assert.Null(c.ReleasedAt));
    }

    [Fact]
    public async Task UpgradeRepairIsExplicitlyNotApplicableEvenWithExistingGrant()
    {
        var (f, o, service) = await Prepared();
        await service.ApplyVerifiedPaymentAsync(new(o.ProviderOrderCode, o.Amount, true));
        var order = new RepairOrderEvidence(o.Id, o.UserId, o.PlanId, o.PlanVersionId, o.PlanVersionBinding,
            o.Status, o.Amount, o.PaidAt) { Type = PaymentOrderType.Upgrade };
        var v = f.Subscriptions.Versions.Single(v => v.Id == o.PlanVersionId);
        var purchase = new RepairPurchaseEvidence(order, new(v.Id, v.PlanId, "MEMBERSHIP", v.Price, v.DurationDays));
        var assessed = EntitlementRepairAssessmentPolicy.Assess(new(true, purchase, [purchase], f.Subscriptions.Periods), Now);
        Assert.False(assessed.Eligibility.Eligible); Assert.Equal("NotApplicable", assessed.Entitlement.GrantStatus);
        Assert.Equal("upgrade_repair_not_supported", assessed.Eligibility.Code);
    }

    [Fact]
    public async Task EffectiveEndResolverAndPaidThroughIgnoreEmptyFutureAndPreserveRawHistory()
    {
        var (f, o, service) = await Prepared();
        var (_, future) = SubscriptionCheckoutQuoteTests.AddNative(f, Now.AddDays(5));
        future.Terminate(Now, o.Id);
        var lower = Assert.Single(f.Subscriptions.Periods, p => p.Id != future.Id);
        lower.Terminate(Now.AddHours(1), o.Id);
        var before = await EffectiveSubscriptionResolver.ResolveAsync(f.Subscriptions, User, Now);
        Assert.Equal(Now.AddHours(1), before.EffectiveUntil);
        Assert.Equal(before.EffectiveUntil, before.PaidThrough);
        var after = await EffectiveSubscriptionResolver.ResolveAsync(f.Subscriptions, User, Now.AddHours(1));
        Assert.Equal("FREE", after.Plan.Code);
        Assert.Equal(Now.AddDays(5), lower.EndsAt);
        Assert.False(SubscriptionPeriodLifecycle.IsEffectiveAt(future, future.StartsAt));
    }
}
