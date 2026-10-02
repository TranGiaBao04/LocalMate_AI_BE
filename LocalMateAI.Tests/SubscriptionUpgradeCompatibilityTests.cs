using System.Text.Json;
using LocalMateAI.Application.DTOs.Email;
using LocalMateAI.Application.DTOs.Payments;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.Payments;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Email;

namespace LocalMateAI.Tests;

public sealed class SubscriptionUpgradeCompatibilityTests
{
    internal static readonly DateTime Now = SubscriptionUpgradeReservationPostgresTests.Now;
    internal static EntitlementRepairEvidence Evidence(bool targetPresent = false)
    {
        var lower = EntitlementRepairTests.Evidence();
        var original = lower.Target with { Order = lower.Target.Order with { PaidAt = Now.AddDays(-2) } };
        var source = EntitlementRepairTests.Period(original, Now.AddDays(-2));
        var v = new RepairVersionEvidence(Guid.NewGuid(), Guid.NewGuid(), "CUSTOM_UPPER", 59000, 30);
        var order = new RepairOrderEvidence(Guid.NewGuid(), lower.Target.Order.UserId, v.PlanId, v.Id,
            PlanVersionBinding.Native, PaymentOrderStatus.Paid, 49000, Now)
            { Type = PaymentOrderType.Upgrade, CreditAmount = 10000 };
        var purchase = new RepairPurchaseEvidence(order, v);
        source.Terminate(Now, order.Id);
        var claim = new PaymentOrderCredit { OrderId = order.Id, UserId = order.UserId, PeriodId = source.Id,
            OriginalEndsAt = source.EndsAt, RemainingDays = 4, CalculatedCreditAmount = 10857 };
        return new(true, purchase, [original, purchase], targetPresent
            ? [source, EntitlementRepairTests.Period(purchase, Now)] : [source]) { Claims = [claim] };
    }

    [Fact]
    public void ExactPaidUpgradeGrantIsAlreadyGranted_UsesCashPlusCredit()
    {
        var e = Evidence(true);
        var a = EntitlementRepairTests.Assess(e);
        Assert.Equal("Granted", a.Entitlement.GrantStatus); Assert.Equal("already_granted", a.Eligibility.Code);
        Assert.False(a.Eligibility.Eligible); Assert.Equal(Now.AddDays(30), a.Entitlement.EndsAt);
    }

    [Fact]
    public void MissingTargetRestoresOnlyExactHistoricalFullTerm_NotRepairTime()
    {
        var a = EntitlementRepairTests.Assess(Evidence());
        Assert.True(a.Eligibility.Eligible); Assert.Equal(Now, a.Eligibility.ProposedStartsAt);
        Assert.Equal(Now.AddDays(30), a.Eligibility.ProposedEndsAt);
        Assert.Equal(EntitlementRepairAssessmentPolicy.UpgradeReplayMode, a.Eligibility.ReconstructionMode);
    }

    [Fact]
    public void OriginalLowerOrderRemainsGrantedAfterProvenTermination()
    {
        var e = Evidence(true); e = e with { Target = e.Purchases[0] };
        var p = e.Periods[0];
        var a = EntitlementRepairTests.Assess(e);
        Assert.Equal("Granted", a.Entitlement.GrantStatus); Assert.False(a.Eligibility.Eligible);
        Assert.Equal(p.EndsAt, a.Entitlement.EndsAt); Assert.Equal(p.Id, a.Entitlement.SubscriptionPeriodId);
    }

    [Fact]
    public void UpgradeTargetLaterTerminatedByAnotherProvenUpgradeStillGranted()
    {
        var e = Evidence(true); var targetPeriod = e.Periods[1];
        var version = new RepairVersionEvidence(Guid.NewGuid(), Guid.NewGuid(), "TOP", 100000, 20);
        var next = new RepairPurchaseEvidence(new(Guid.NewGuid(), e.Target.Order.UserId, version.PlanId, version.Id,
            PlanVersionBinding.Native, PaymentOrderStatus.Paid, 70000, Now.AddDays(1))
            { Type = PaymentOrderType.Upgrade, CreditAmount = 30000 }, version);
        targetPeriod.Terminate(next.Order.PaidAt!.Value, next.Order.Id);
        e = e with { Purchases = [.. e.Purchases, next], Claims = [.. e.Claims, new()
            { OrderId = next.Order.Id, PeriodId = targetPeriod.Id, UserId = next.Order.UserId,
                OriginalEndsAt = targetPeriod.EndsAt, RemainingDays = 28, CalculatedCreditAmount = 55066 }] };
        Assert.Equal("already_granted", EntitlementRepairTests.Assess(e).Eligibility.Code);
    }

    [Theory]
    [InlineData(-1, 5)]
    [InlineData(1, 1)]
    public void OrdinaryRenewalReplayUsesTerminationOnlyOnceItExistedAtPaidTime(int paidOffset, int startOffset)
    {
        var e = Evidence(true); var lower = e.Purchases[0];
        var renewal = lower with { Order = lower.Order with { Id = Guid.NewGuid(), Type = PaymentOrderType.Renewal,
            PaidAt = Now.AddDays(paidOffset) } };
        e = e with { Target = renewal, Purchases = [.. e.Purchases, renewal] };
        var a = EntitlementRepairTests.Assess(e);
        Assert.True(a.Eligibility.Eligible);
        Assert.Equal(Now.AddDays(startOffset), a.Eligibility.ProposedStartsAt);
        Assert.Equal(Now.AddDays(startOffset + 7), a.Eligibility.ProposedEndsAt);
    }

    [Theory]
    [InlineData("amount")]
    [InlineData("negative_credit")]
    [InlineData("released")]
    [InlineData("mixed")]
    [InlineData("missing_claim")]
    [InlineData("missing_source")]
    [InlineData("ends_snapshot")]
    [InlineData("not_terminated")]
    [InlineData("wrong_terminator")]
    [InlineData("termination_time")]
    [InlineData("claim_owner")]
    [InlineData("claim_order")]
    [InlineData("review")]
    [InlineData("paid_at")]
    [InlineData("binding")]
    [InlineData("target_duplicate")]
    [InlineData("target_owner")]
    [InlineData("target_window")]
    [InlineData("overlap")]
    [InlineData("unproved_target_termination")]
    public void IncompleteOrContradictoryEvidenceNeverRepairs(string flaw)
    {
        var e = Evidence(flaw.StartsWith("target", StringComparison.Ordinal) || flaw == "unproved_target_termination");
        var c = e.Claims[0]; var source = e.Periods[0];
        var o = e.Target.Order;
        switch (flaw)
        {
            case "amount": o = o with { Amount = 59000 }; break;
            case "negative_credit": o = o with { CreditAmount = -1 }; break;
            case "released": c.ReleasedAt = Now; break;
            case "mixed": e = e with { Claims = [c, new() { OrderId = o.Id, UserId = o.UserId,
                PeriodId = Guid.NewGuid(), ReleasedAt = Now }] }; break;
            case "missing_claim": e = e with { Claims = [] }; break;
            case "missing_source": e = e with { Periods = [] }; break;
            case "ends_snapshot": e = e with { Claims = [new() { OrderId = o.Id, UserId = o.UserId,
                PeriodId = c.PeriodId, OriginalEndsAt = c.OriginalEndsAt.AddSeconds(1) }] }; break;
            case "not_terminated":
            case "wrong_terminator":
            case "termination_time":
                var fresh = EntitlementRepairTests.Period(e.Purchases[0], source.StartsAt);
                fresh = new() { Id = source.Id, UserId = fresh.UserId, PlanId = fresh.PlanId, PlanVersionId = fresh.PlanVersionId,
                    SourcePaymentOrderId = fresh.SourcePaymentOrderId, StartsAt = fresh.StartsAt, EndsAt = fresh.EndsAt };
                if (flaw != "not_terminated") fresh.Terminate(flaw == "termination_time" ? Now.AddSeconds(1) : Now,
                    flaw == "wrong_terminator" ? Guid.NewGuid() : o.Id);
                e = e with { Periods = [fresh] }; break;
            case "claim_owner": e = e with { Claims = [new() { OrderId = o.Id, UserId = Guid.NewGuid(),
                PeriodId = c.PeriodId, OriginalEndsAt = c.OriginalEndsAt }] }; break;
            case "claim_order": e = e with { Claims = [new() { OrderId = Guid.NewGuid(), UserId = o.UserId,
                PeriodId = c.PeriodId, OriginalEndsAt = c.OriginalEndsAt }] }; break;
            case "review": o = o with { Status = PaymentOrderStatus.ReviewRequired }; break;
            case "paid_at": o = o with { PaidAt = null }; break;
            case "binding": o = o with { Binding = PlanVersionBinding.LegacyVerified }; break;
            case "target_duplicate": e = e with { Periods = [.. e.Periods, e.Periods[1]] }; break;
            case "target_owner":
            case "target_window":
            case "overlap":
                e = e with { Periods = [source, new SubscriptionPeriod
                { UserId = flaw == "target_owner" ? Guid.NewGuid() : o.UserId,
                    PlanId = o.PlanId!.Value, PlanVersionId = o.PlanVersionId!.Value,
                    SourcePaymentOrderId = flaw == "overlap" ? Guid.NewGuid() : o.Id,
                    StartsAt = flaw == "target_window" ? Now.AddSeconds(1) : flaw == "overlap" ? Now.AddDays(1) : Now,
                    EndsAt = Now.AddDays(30) }] }; break;
            case "unproved_target_termination": e.Periods[1].Terminate(Now.AddDays(2), Guid.NewGuid()); break;
        }
        e = e with { Target = e.Target with { Order = o }, Purchases = e.Purchases.Select(p => p.Order.Id == o.Id ? p with { Order = o } : p).ToArray() };
        var a = EntitlementRepairTests.Assess(e);
        Assert.False(a.Eligibility.Eligible); Assert.NotEqual("Granted", a.Entitlement.GrantStatus);
        if (flaw == "review") Assert.Equal("payment_review_required", a.Eligibility.Code);
    }

    [Theory]
    [InlineData(PlanVersionBinding.LegacyUnresolved)]
    [InlineData(PlanVersionBinding.LegacyVerified)]
    [InlineData(PlanVersionBinding.LegacyApprovedBaseline)]
    public void UpgradeLegacyBindingsFailClosed(PlanVersionBinding binding)
    {
        var e = Evidence(); e = e with { Target = e.Target with { Order = e.Target.Order with { Binding = binding } } };
        Assert.Equal("historical_binding_not_supported", EntitlementRepairTests.Assess(e).Eligibility.Code);
    }

    [Fact]
    public void OrdinaryPurchaseRejectsUnexpectedCredit_AndSingleRemainsNotApplicable()
    {
        var e = EntitlementRepairTests.Evidence();
        Assert.Equal("amount_mismatch", EntitlementRepairTests.Assess(e with { Target = e.Target with
            { Order = e.Target.Order with { CreditAmount = 1 } } }).Eligibility.Code);
        var single = Evidence();
        single = single with { Target = single.Target with { Order = single.Target.Order with { ProductKind = PaymentProductKind.SingleItinerary } } };
        Assert.Equal("NotApplicable", EntitlementRepairTests.Assess(single).Entitlement.GrantStatus);
    }

    [Fact]
    public async Task DedicatedReceiptSnapshotAndRegistryAndSafeFluidRendering()
    {
        var e = Evidence(true); var o = e.Target.Order;
        var order = new PaymentOrder { Id = o.Id, UserId = o.UserId, Amount = o.Amount, CreditAmount = o.CreditAmount,
            Type = PaymentOrderType.Upgrade, PaidAt = o.PaidAt, ProviderOrderCode = 123456 };
        var entry = UpgradePaymentReceiptEmailBuilder.Build(new() { FullName = "An <script>", Email = "an@receipt.test" },
            order, e.Periods[1], new() { Name = "Gói <Plus>" }, new() { DurationDays = 30, Price = 59000 });
        Assert.Equal(EmailTemplateNames.UpgradePaymentReceipt, entry.TemplateName);
        Assert.Equal($"payment-receipt:{o.Id}", entry.DeduplicationKey);
        var model = Assert.IsType<UpgradePaymentReceiptEmailModel>(EmailOutboxModelRegistry.Default.Deserialize(entry.TemplateName, entry.ModelJson));
        Assert.Equal("Nâng cấp", model.TypeLabel); Assert.Equal("59.000đ", model.ListPrice);
        Assert.Equal("10.000đ", model.CreditAmount); Assert.Equal("49.000đ", model.Amount); Assert.Equal("30 ngày", model.Duration);
        Assert.Equal(EmailDisplayFormat.VietnamDateTime(Now), model.EffectiveFrom);
        Assert.Equal(EmailDisplayFormat.VietnamDateTime(Now.AddDays(30)), model.ValidUntil);
        var html = await new FluidEmailTemplateRenderer().RenderAsync(entry.TemplateName, model);
        Assert.Contains("An &lt;script&gt;", html); Assert.DoesNotContain("<script>", html);
        Assert.Contains("Gói &lt;Plus&gt;", html); Assert.Contains("49.000đ", html);
        Assert.Contains("không cộng thêm ngày", html);
        var legacy = new PaymentReceiptEmailModel("An", "1", "Trip", "Mua mới", "19.000đ", "date", "7 ngày", "end");
        Assert.Equal(legacy, EmailOutboxModelRegistry.Default.Deserialize(EmailTemplateNames.PaymentReceipt, EmailOutboxModelRegistry.Serialize(legacy)));
    }

    [Theory]
    [InlineData("Purchase", 0)]
    [InlineData("Renewal", 0)]
    [InlineData("Single", 0)]
    [InlineData("Upgrade", 10000)]
    public void AdditiveFinancialResponseAndCsvUseOnlySnapshot(string type, int credit)
    {
        var r = new AdminTransactionResponse(Guid.NewGuid(), 1, Guid.NewGuid(), "=An", "mail", type == "Single" ? "SingleItinerary" : "SubscriptionPlan",
            null, null, type == "Single" ? "Purchase" : type, "Paid", 49000, "VND", Now, Now, Now) { CreditAmount = credit };
        Assert.Equal(49000 + credit, r.ListPrice);
        var content = AdminTransactionCsv.Write([r]); Assert.Equal(new byte[] { 239, 187, 191 }, content[..3]);
        var csv = AdminTransactionTests.ParseCsv(content);
        Assert.Equal("CreditAmount", csv[0][14]); Assert.Equal("49000", csv[1][13]); Assert.Equal(credit.ToString(), csv[1][14]);
        Assert.Equal("'=An", csv[1][6]);
    }

    [Theory]
    [InlineData("ReviewRequired", "Upgrade")]
    [InlineData("reviewrequired", "upgrade")]
    public void AdminFiltersAcceptReviewAndUpgrade(string status, string type)
    {
        var q = new AdminTransactionFilterQuery { Status = status, OperationType = type };
        Assert.True(new AdminTransactionFilterQueryValidator().Validate(q).IsValid);
        Assert.Equal(PaymentOrderStatus.ReviewRequired, AdminTransactionFilterQueryValidator.Normalize(q).Status);
    }

    [Fact]
    public void CreditSourcePublicSchemaContainsNoInternalIdentifiers()
    {
        var r = new AdminTransactionCreditSourceResponse("TRIP_PASS", "Trip", Now, 4, 10857, "Consumed", Now, null, null);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(r, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        Assert.DoesNotContain(json.RootElement.EnumerateObject(), p => p.Name.EndsWith("Id", StringComparison.OrdinalIgnoreCase));
    }
}
