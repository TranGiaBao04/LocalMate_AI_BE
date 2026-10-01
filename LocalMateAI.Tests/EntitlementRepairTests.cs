using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class EntitlementRepairTests
{
    internal static readonly DateTime Day = new(2026, 10, 1, 2, 0, 0, DateTimeKind.Utc);
    internal static EntitlementRepairEvidence Evidence(int count = 1)
    {
        var user = Guid.NewGuid(); var plan = Guid.NewGuid(); var version = new RepairVersionEvidence(Guid.NewGuid(), plan, "CUSTOM_PLAN", 19000, 7);
        var purchases = Enumerable.Range(0, count).Select(i => new RepairPurchaseEvidence(
            new(Guid.NewGuid(), user, plan, version.Id, PlanVersionBinding.Native, PaymentOrderStatus.Paid, 19000, Day.AddDays(i)), version)).ToArray();
        return new(true, purchases[0], purchases, []);
    }
    internal static SubscriptionPeriod Period(RepairPurchaseEvidence purchase, DateTime start) => new()
    {
        UserId = purchase.Order.UserId, PlanId = purchase.Order.PlanId!.Value, PlanVersionId = purchase.Order.PlanVersionId!.Value,
        SourcePaymentOrderId = purchase.Order.Id, StartsAt = start, EndsAt = start.AddDays(purchase.Version!.DurationDays!.Value)
    };
    internal static EntitlementRepairAssessment Assess(EntitlementRepairEvidence evidence) =>
        EntitlementRepairAssessmentPolicy.Assess(evidence, Day.AddDays(90));
    private static EntitlementRepairEvidence Target(EntitlementRepairEvidence e, RepairPurchaseEvidence p) => e with
    { Target = p, Purchases = [p, .. e.Purchases.Where(x => x.Order.Id != p.Order.Id)] };

    [Fact]
    public void NativeCustomSnapshot_ExpiredHistoricalRange_IsNotRepairTime()
    {
        var e = Evidence(); var result = Assess(e);
        Assert.True(result.Eligibility.Eligible);
        Assert.Equal(Day, result.Eligibility.ProposedStartsAt);
        Assert.Equal(Day.AddDays(7), result.Eligibility.ProposedEndsAt);
        Assert.Equal(Day.AddDays(90), result.Eligibility.AssessedAt);
        Assert.Equal("DeterministicHistoricalReplay", result.Eligibility.ReconstructionMode);
        Assert.Empty(e.Periods);
    }

    [Theory]
    [InlineData(PlanVersionBinding.LegacyUnresolved)]
    [InlineData(PlanVersionBinding.LegacyVerified)]
    [InlineData(PlanVersionBinding.LegacyApprovedBaseline)]
    public void AllLegacyMissingBindings_FailClosed(PlanVersionBinding binding)
    {
        var e = Evidence(); e = Target(e, e.Target with { Order = e.Target.Order with { Binding = binding } });
        Assert.Equal("historical_binding_not_supported", Assess(e).Eligibility.Code);
        Assert.False(Assess(e).Eligibility.Eligible);
    }

    [Theory]
    [InlineData(PaymentOrderStatus.Pending)]
    [InlineData(PaymentOrderStatus.Failed)]
    [InlineData(PaymentOrderStatus.Expired)]
    public void NonPaid_CannotBeRepaired(PaymentOrderStatus status)
    {
        var e = Evidence(); e = Target(e, e.Target with { Order = e.Target.Order with { Status = status } });
        Assert.Equal("order_not_paid", Assess(e).Eligibility.Code);
        Assert.Equal("NotApplicable", Assess(e).Entitlement.GrantStatus);
    }

    [Theory]
    [InlineData("no_user", "invalid_purchased_version")]
    [InlineData("no_plan", "invalid_purchased_version")]
    [InlineData("no_version", "invalid_purchased_version")]
    [InlineData("unknown_version", "invalid_purchased_version")]
    [InlineData("wrong_plan", "invalid_purchased_version")]
    [InlineData("wrong_version", "invalid_purchased_version")]
    [InlineData("zero_price", "invalid_purchased_version")]
    [InlineData("negative_price", "invalid_purchased_version")]
    [InlineData("fraction_price", "invalid_purchased_version")]
    [InlineData("no_duration", "invalid_purchased_version")]
    [InlineData("zero_duration", "invalid_purchased_version")]
    [InlineData("negative_duration", "invalid_purchased_version")]
    [InlineData("amount", "amount_mismatch")]
    [InlineData("free", "free_plan_not_repairable")]
    [InlineData("no_paid_at", "paid_at_missing")]
    [InlineData("local_time", "historical_window_unproven")]
    [InlineData("overflow", "historical_window_unproven")]
    public void EligibilityMatrix(string mutation, string code)
    {
        var e = Evidence(); var p = e.Target; var v = p.Version!;
        p = mutation switch
        {
            "no_plan" => p with { Order = p.Order with { PlanId = null } },
            "no_version" => p with { Order = p.Order with { PlanVersionId = null } },
            "unknown_version" => p with { Version = null },
            "wrong_plan" => p with { Version = v with { PlanId = Guid.NewGuid() } },
            "wrong_version" => p with { Version = v with { Id = Guid.NewGuid() } },
            "zero_price" => p with { Version = v with { Price = 0 } },
            "negative_price" => p with { Version = v with { Price = -1 } },
            "fraction_price" => p with { Version = v with { Price = 1.5m } },
            "no_duration" => p with { Version = v with { DurationDays = null } },
            "zero_duration" => p with { Version = v with { DurationDays = 0 } },
            "negative_duration" => p with { Version = v with { DurationDays = -1 } },
            "amount" => p with { Order = p.Order with { Amount = 49000 } },
            "free" => p with { Version = v with { PlanCode = "FREE", Price = 0 } },
            "no_paid_at" => p with { Order = p.Order with { PaidAt = null } },
            "local_time" => p with { Order = p.Order with { PaidAt = DateTime.SpecifyKind(Day, DateTimeKind.Local) } },
            "overflow" => p with { Version = v with { DurationDays = int.MaxValue } },
            _ => p
        };
        e = Target(e, p) with { UserExists = mutation != "no_user" };
        Assert.Equal(code, Assess(e).Eligibility.Code);
        Assert.False(Assess(e).Eligibility.Eligible);
    }

    [Theory]
    [InlineData(-30)]
    [InlineData(120)]
    public void ExistingExpiredOrFutureGrant_AlwaysAlreadyGranted(int offset)
    {
        var e = Evidence(); var period = Period(e.Target, Day.AddDays(offset));
        e = e with { Periods = [period] };
        var result = Assess(e);
        Assert.Equal("already_granted", result.Eligibility.Code);
        Assert.Equal("Granted", result.Entitlement.GrantStatus);
        Assert.Equal(period.Id, result.Entitlement.SubscriptionPeriodId);
        Assert.False(result.Eligibility.Eligible);
    }

    [Fact]
    public void ReplayMissingMiddle_UsesOriginalTail_AndNeverMovesLaterPeriod()
    {
        var e = Evidence(3); var first = Period(e.Purchases[0], Day); var last = Period(e.Purchases[2], Day.AddDays(14));
        e = e with { Target = e.Purchases[1], Periods = [first, last] };
        var result = Assess(e);
        Assert.True(result.Eligibility.Eligible);
        Assert.Equal(Day.AddDays(7), result.Eligibility.ProposedStartsAt);
        Assert.Equal(Day.AddDays(14), result.Eligibility.ProposedEndsAt);
        Assert.Equal(Day.AddDays(14), last.StartsAt);
    }

    [Fact]
    public void MultipleMissingOrders_ReplayVirtualTail_WithoutCreatingOtherGrants()
    {
        var e = Evidence(3);
        var result = Assess(e with { Target = e.Purchases[2] });
        Assert.Equal(Day.AddDays(14), result.Eligibility.ProposedStartsAt);
        Assert.Empty(e.Periods);
    }

    [Theory]
    [InlineData("tie")]
    [InlineData("missing_anchor")]
    [InlineData("legacy_paid")]
    [InlineData("other_invalid_version")]
    [InlineData("missing_target")]
    public void IncompleteOrAmbiguousChain_FailsClosed(string mutation)
    {
        var e = Evidence(2); var second = e.Purchases[1];
        second = mutation switch
        {
            "tie" => second with { Order = second.Order with { PaidAt = Day } },
            "missing_anchor" => second with { Order = second.Order with { PaidAt = null } },
            "legacy_paid" => second with { Order = second.Order with { Binding = PlanVersionBinding.LegacyVerified } },
            "other_invalid_version" => second with { Version = null },
            _ => second
        };
        e = e with { Purchases = mutation == "missing_target" ? [second] : [e.Target, second] };
        Assert.Equal("historical_window_unproven", Assess(e).Eligibility.Code);
    }

    [Fact]
    public void LegacyAggregate_IsNotProofThatAnOrderWasNeverGranted()
    {
        var e = Evidence();
        e = e with { Periods = [new() { UserId = e.Target.Order.UserId, PlanId = e.Target.Order.PlanId!.Value,
            PlanVersionId = e.Target.Order.PlanVersionId!.Value, LegacyUserSubscriptionId = Guid.NewGuid(),
            StartsAt = Day.AddDays(-60), EndsAt = Day.AddDays(-30) }] };
        Assert.Equal("legacy_entitlement_ambiguous", Assess(e).Eligibility.Code);
    }

    [Theory]
    [InlineData("range")]
    [InlineData("owner")]
    [InlineData("version")]
    [InlineData("source")]
    [InlineData("duration")]
    public void PersistedEvidenceContradictions_AreConflict(string mutation)
    {
        var e = Evidence(2); var p = Period(e.Purchases[1], Day.AddDays(7));
        p = mutation switch
        {
            "range" => Period(e.Purchases[1], Day.AddDays(8)),
            "owner" => new() { UserId = Guid.NewGuid(), PlanId = p.PlanId, PlanVersionId = p.PlanVersionId,
                SourcePaymentOrderId = p.SourcePaymentOrderId, StartsAt = p.StartsAt, EndsAt = p.EndsAt },
            "version" => new() { UserId = p.UserId, PlanId = p.PlanId, PlanVersionId = Guid.NewGuid(),
                SourcePaymentOrderId = p.SourcePaymentOrderId, StartsAt = p.StartsAt, EndsAt = p.EndsAt },
            "source" => new() { UserId = p.UserId, PlanId = p.PlanId, PlanVersionId = p.PlanVersionId,
                SourcePaymentOrderId = Guid.NewGuid(), StartsAt = p.StartsAt, EndsAt = p.EndsAt },
            "duration" => new() { UserId = p.UserId, PlanId = p.PlanId, PlanVersionId = p.PlanVersionId,
                SourcePaymentOrderId = p.SourcePaymentOrderId, StartsAt = p.StartsAt, EndsAt = p.EndsAt.AddTicks(1) },
            _ => p
        };
        var result = Assess(e with { Periods = [p] });
        Assert.Equal("historical_replay_conflict", result.Eligibility.Code);
        Assert.True(result.IsConflict);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\nreason")]
    [InlineData("\tbad")]
    [InlineData("bad\u0000")]
    public async Task InvalidReason_NeverReachesExecutor(string? reason)
    {
        var executor = new RecordingExecutor();
        Assert.True((await new EntitlementRepairService(executor).RepairAsync(Guid.NewGuid(), Guid.NewGuid(), reason)).InvalidRequest);
        Assert.Equal(0, executor.Calls);
    }

    [Fact]
    public async Task ReasonLengthAndTrim_ServicePreservesIdentityAndCancellation()
    {
        var executor = new RecordingExecutor(); var service = new EntitlementRepairService(executor);
        Assert.True((await service.RepairAsync(Guid.NewGuid(), Guid.NewGuid(), new string('a', 501))).InvalidRequest);
        using var cts = new CancellationTokenSource();
        Assert.False((await service.RepairAsync(Guid.NewGuid(), Guid.NewGuid(), "  Khôi phục lịch sử  ", cts.Token)).InvalidRequest);
        Assert.Equal("Khôi phục lịch sử", executor.Reason);
        Assert.Equal(cts.Token, executor.Token);
        Assert.True(EntitlementRepairReason.TryNormalize(new string('a', 500), out _));
    }
    private sealed class RecordingExecutor : IEntitlementRepairExecutor
    {
        public int Calls; public string? Reason; public CancellationToken Token;
        public Task<EntitlementRepairResponse?> ExecuteAsync(Guid orderId, Guid actorId, string reason, CancellationToken ct = default)
        { Calls++; Reason = reason; Token = ct; return Task.FromResult<EntitlementRepairResponse?>(null); }
    }
}
