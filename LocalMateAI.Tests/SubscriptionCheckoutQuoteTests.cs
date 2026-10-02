using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;

namespace LocalMateAI.Tests;

public sealed class SubscriptionCheckoutQuoteTests
{
    internal static readonly DateTime Now = new(2026, 10, 3, 0, 0, 0, DateTimeKind.Utc);
    internal static readonly Guid UserId = Guid.Parse("88888888-1111-2222-3333-444444444444");

    internal static (PaymentOrder Order, SubscriptionPeriod Period) AddNative(
        PaymentServiceTests.Fixture f, DateTime? starts = null, PlanCode code = PlanCode.TripPass)
    {
        var v = f.Subscriptions.Versions.Single(v => v.Id == SubscriptionBaseline.VersionId(code));
        var o = new PaymentOrder { UserId = UserId, PlanId = v.PlanId, PlanVersionId = v.Id,
            PlanVersionBinding = PlanVersionBinding.Native, Status = PaymentOrderStatus.Paid,
            PaidAt = starts ?? Now.AddDays(-2), Amount = v.Price };
        var p = new SubscriptionPeriod { UserId = UserId, PlanId = v.PlanId, PlanVersionId = v.Id,
            SourcePaymentOrderId = o.Id, StartsAt = starts ?? Now.AddDays(-2),
            EndsAt = (starts ?? Now.AddDays(-2)).AddDays(v.DurationDays!.Value) };
        f.Subscriptions.QuoteOrders.Add(o);
        f.Subscriptions.Periods.Add(p);
        return (o, p);
    }

    private static PaymentServiceTests.Fixture Fixture() => new(now: Now);

    [Theory]
    [InlineData(" TripPass ", 19000, 7)]
    [InlineData("trippass", 19000, 7)]
    [InlineData(" TRIP_PASS ", 19000, 7)]
    [InlineData(" membership ", 59000, 30)]
    [InlineData("MEMBERSHIP", 59000, 30)]
    public async Task FreePurchaseQuoteIsFullPrice_ReadOnly(string code, int price, int days)
    {
        var f = Fixture();
        var q = (await f.Service.GetCheckoutQuoteAsync(UserId, code)).Response!;
        Assert.Equal("Purchase", q.Type);
        Assert.Equal(price, q.ListPrice);
        Assert.Equal(price, q.Amount);
        Assert.Equal(0, q.CreditAmount);
        Assert.Equal(days, q.DurationDays);
        Assert.Empty(q.Credits);
        Assert.Empty(f.Orders.Items);
        Assert.Empty(f.Gateway.Requests);
        Assert.Equal(0, f.Gateway.LookupCalls);
    }

    [Theory]
    [InlineData(0, 16285, 16000, 43000, 6)]
    [InlineData(2, 10857, 10000, 49000, 4)]
    public async Task NativePurchasedTermsUseExistingCalculator(int elapsedDays, int raw, int applied, int amount, int remaining)
    {
        var f = Fixture();
        AddNative(f, Now.AddDays(-elapsedDays));
        var q = (await f.Service.GetCheckoutQuoteAsync(UserId, "Membership")).Response!;
        Assert.Equal("Upgrade", q.Type);
        Assert.Equal(59000, q.ListPrice);
        Assert.Equal(applied, q.CreditAmount);
        Assert.Equal(amount, q.Amount);
        var row = Assert.Single(q.Credits);
        Assert.Equal(raw, row.CreditAmount);
        Assert.Equal(remaining, row.RemainingDays);
    }

    [Fact]
    public async Task OldPurchasedVersionIsUsed_NotCurrentPriceOrNetAmount()
    {
        var f = Fixture();
        var (o, p) = AddNative(f);
        var old = f.Subscriptions.Versions.Single(v => v.Id == p.PlanVersionId);
        f.Subscriptions.Versions.Remove(old);
        f.Subscriptions.Versions.Add(new() { Id = old.Id, PlanId = old.PlanId, Price = 49000, DurationDays = 7 });
        o.Amount = 49000;
        f.Subscriptions.Versions.Add(new() { PlanId = p.PlanId, Price = 99000, DurationDays = 70 });
        f.Subscriptions.Plans.Single(x => x.Id == p.PlanId).CurrentVersionId = f.Subscriptions.Versions.Last().Id;
        var q = (await f.Service.GetCheckoutQuoteAsync(UserId, "Membership")).Response!;
        Assert.Equal(28000, Assert.Single(q.Credits).CreditAmount);
        Assert.Equal(31000, q.Amount);
    }

    [Fact]
    public async Task CurrentFutureAndMaskedLowerSources_AllRetained()
    {
        var f = Fixture();
        AddNative(f);
        AddNative(f, Now.AddDays(5));
        AddNative(f, code: PlanCode.Membership);
        var target = new SubscriptionPlan { Code = "CUSTOM_TOP", Name = "Custom", IsActive = true, EntitlementPriority = 300 };
        var v = new SubscriptionPlanVersion { PlanId = target.Id, Price = 90000, DurationDays = 30 };
        target.CurrentVersionId = v.Id;
        f.Subscriptions.Plans.Add(target); f.Subscriptions.Versions.Add(v);
        var q = (await f.Service.GetCheckoutQuoteAsync(UserId, " custom_top ")).Response!;
        Assert.Equal("Upgrade", q.Type);
        Assert.Equal(3, q.Credits.Count);
        Assert.Equal(19000, q.Credits.Single(x => x.RemainingDays == 7).CreditAmount);
    }

    [Theory]
    [InlineData("no_source")]
    [InlineData("no_order")]
    [InlineData("legacy")]
    [InlineData("owner")]
    [InlineData("product")]
    [InlineData("pending")]
    [InlineData("paid_at")]
    [InlineData("binding")]
    [InlineData("plan")]
    [InlineData("version")]
    [InlineData("amount")]
    [InlineData("credit")]
    [InlineData("no_version")]
    [InlineData("duration")]
    [InlineData("price")]
    public async Task UnprovenEvidenceIsZero_NotDropped(string flaw)
    {
        var f = Fixture();
        var (o, p) = AddNative(f);
        var v = f.Subscriptions.Versions.Single(v => v.Id == p.PlanVersionId);
        p = new() { Id = p.Id, UserId = p.UserId, PlanId = p.PlanId, PlanVersionId = p.PlanVersionId,
            StartsAt = p.StartsAt, EndsAt = p.EndsAt,
            SourcePaymentOrderId = flaw == "no_source" ? null : o.Id,
            LegacyUserSubscriptionId = flaw == "legacy" ? Guid.NewGuid() : null };
        v = new() { Id = v.Id, PlanId = v.PlanId, Price = flaw == "price" ? 0 : v.Price,
            DurationDays = flaw == "duration" ? null : v.DurationDays };
        switch (flaw)
        {
            case "no_order": f.Subscriptions.QuoteOrders.Clear(); break;
            case "owner": o.UserId = Guid.NewGuid(); break;
            case "product": o.ProductKind = PaymentProductKind.SingleItinerary; break;
            case "pending": o.Status = PaymentOrderStatus.Pending; break;
            case "paid_at": o.PaidAt = null; break;
            case "binding": o.PlanVersionBinding = PlanVersionBinding.LegacyVerified; break;
            case "plan": o.PlanId = Guid.NewGuid(); break;
            case "version": o.PlanVersionId = Guid.NewGuid(); break;
            case "amount": o.Amount--; break;
            case "credit": o.CreditAmount = 1; break;
            case "no_version": f.Subscriptions.Versions.Remove(v); break;
        }
        // Keep a valid effective lower period even if the evidence row is corrupt.
        var sources = SubscriptionQuoteEvidence.Build(UserId, 200, Now,
            [new(p, f.Subscriptions.Plans.Single(x => x.Id == p.PlanId),
                flaw == "no_version" ? null : v, flaw == "no_order" ? null : o)]);
        var row = Assert.Single(sources);
        Assert.False(row.MonetaryCreditEligible);
        var result = UpgradeCreditCalculator.Calculate(new(59000, Now, sources));
        Assert.Equal(0, Assert.Single(result.Sources).CalculatedCreditAmount);
        Assert.Equal(59000, result.AmountPayable);
    }

    [Theory]
    [InlineData(PaymentOrderType.Purchase, 19000, 0, true)]
    [InlineData(PaymentOrderType.Renewal, 19000, 0, true)]
    [InlineData(PaymentOrderType.Upgrade, 10000, 9000, true)]
    [InlineData(PaymentOrderType.Upgrade, 10000, 8000, false)]
    [InlineData(PaymentOrderType.Purchase, 10000, 9000, false)]
    public void FinancialConservationUsesPurchasedGrossValue(PaymentOrderType type, int amount, int credit, bool eligible)
    {
        var f = Fixture();
        var (o, p) = AddNative(f); o.Type = type; o.Amount = amount; o.CreditAmount = credit;
        var v = f.Subscriptions.Versions.Single(v => v.Id == p.PlanVersionId);
        var source = Assert.Single(SubscriptionQuoteEvidence.Build(UserId, 200, Now,
            [new(p, f.Subscriptions.Plans.Single(x => x.Id == p.PlanId), v, o)]));
        Assert.Equal(eligible, source.MonetaryCreditEligible);
        Assert.Equal(eligible ? 19000 : 0, source.PurchasedPrice);
    }

    [Fact]
    public async Task LegacyLifecycleUsesPersistedVietnamDates_NotCatalogDuration()
    {
        var f = Fixture();
        var (_, p) = AddNative(f);
        f.Subscriptions.Periods.Clear();
        f.Subscriptions.Periods.Add(new() { Id = p.Id, UserId = p.UserId, PlanId = p.PlanId,
            PlanVersionId = p.PlanVersionId, LegacyUserSubscriptionId = Guid.NewGuid(),
            StartsAt = Now.AddDays(-2), EndsAt = Now.AddDays(10) });
        var q = (await f.Service.GetCheckoutQuoteAsync(UserId, "Membership")).Response!;
        var row = Assert.Single(q.Credits);
        Assert.Equal(10, row.RemainingDays); // 13 touched VN dates minus 3 already used.
        Assert.Equal(0, row.CreditAmount);
    }

    [Theory]
    [InlineData(9000, 9000)]
    [InlineData(15000, 10000)]
    public async Task HigherPriorityCheaperCustomTargetRemainsUpgrade(int price, int amount)
    {
        var f = Fixture(); AddNative(f);
        var target = new SubscriptionPlan { Code = "CHEAPER", Name = "Cheaper", IsActive = true, EntitlementPriority = 150 };
        var v = new SubscriptionPlanVersion { PlanId = target.Id, Price = price, DurationDays = 3 };
        target.CurrentVersionId = v.Id; f.Subscriptions.Plans.Add(target); f.Subscriptions.Versions.Add(v);
        var q = (await f.Service.GetCheckoutQuoteAsync(UserId, "cheaper")).Response!;
        Assert.Equal("Upgrade", q.Type); Assert.Equal(amount, q.Amount);
        Assert.Equal(PaymentIntentResultStatus.UpgradeCheckoutNotReady,
            (await f.Service.CheckoutAsync(UserId, "cheaper")).Status);
        Assert.Empty(f.Orders.Items); Assert.Empty(f.Orders.Histories); Assert.Empty(f.Gateway.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FutureTargetGateIsSharedByQuoteAndCheckout(bool terminated)
    {
        var f = Fixture(); AddNative(f);
        var (_, future) = AddNative(f, Now.AddDays(5), PlanCode.Membership);
        if (terminated) future.Terminate(Now, Guid.NewGuid());
        var q = await f.Service.GetCheckoutQuoteAsync(UserId, "Membership");
        var checkout = await f.Service.CheckoutAsync(UserId, "Membership");
        Assert.Equal(terminated ? PaymentIntentResultStatus.Success : PaymentIntentResultStatus.TargetPlanAlreadyScheduled, q.Status);
        Assert.Equal(terminated ? PaymentIntentResultStatus.UpgradeCheckoutNotReady
            : PaymentIntentResultStatus.TargetPlanAlreadyScheduled, checkout.Status);
        Assert.Empty(f.Orders.Items); Assert.Empty(f.Orders.Histories); Assert.Empty(f.Gateway.Requests);
    }

    [Theory]
    [InlineData("inactive")]
    [InlineData("no_pointer")]
    [InlineData("missing_version")]
    [InlineData("wrong_owner")]
    [InlineData("zero_price")]
    [InlineData("fractional_price")]
    [InlineData("no_duration")]
    [InlineData("zero_duration")]
    public async Task InvalidTargetRejectedBeforePendingMutation(string flaw)
    {
        var f = Fixture();
        var plan = f.Subscriptions.Plans.Single(p => p.Code == "MEMBERSHIP");
        var v = f.Subscriptions.Versions.Single(v => v.PlanId == plan.Id);
        f.Subscriptions.Versions.Remove(v);
        f.Subscriptions.Versions.Add(new() { Id = v.Id,
            PlanId = flaw == "wrong_owner" ? Guid.NewGuid() : v.PlanId,
            Price = flaw == "zero_price" ? 0 : flaw == "fractional_price" ? 1.5m : v.Price,
            DurationDays = flaw == "no_duration" ? null : flaw == "zero_duration" ? 0 : v.DurationDays });
        switch (flaw)
        {
            case "inactive": plan.IsActive = false; break;
            case "no_pointer": plan.CurrentVersionId = null; break;
            case "missing_version": plan.CurrentVersionId = Guid.NewGuid(); break;
        }
        var pending = new PaymentOrder { UserId = UserId, PlanId = plan.Id, Amount = 59000, ExpiresAt = Now.AddMinutes(-1) };
        f.Orders.Items.Add(pending);
        Assert.Equal(PaymentIntentResultStatus.InvalidPlanCode,
            (await f.Service.GetCheckoutQuoteAsync(UserId, "Membership")).Status);
        Assert.Equal(PaymentIntentResultStatus.InvalidPlanCode,
            (await f.Service.CheckoutAsync(UserId, "Membership")).Status);
        Assert.Equal(PaymentOrderStatus.Pending, pending.Status);
        Assert.Empty(f.Orders.Histories); Assert.Empty(f.Gateway.Requests);
    }

    [Fact]
    public async Task QuoteIgnoresPending_CheckoutExpiresOrdinaryPendingButNeverCreatesUpgrade()
    {
        var f = Fixture(); var (_, p) = AddNative(f);
        var pending = new PaymentOrder { UserId = UserId, PlanId = SubscriptionBaseline.PlanId(PlanCode.Membership),
            Amount = 59000, ExpiresAt = Now.AddDays(-1), CheckoutUrl = "private", QrCode = "private" };
        f.Orders.Items.Add(pending);
        var end = p.EndsAt;
        Assert.Equal(PaymentIntentResultStatus.Success, (await f.Service.GetCheckoutQuoteAsync(UserId, "Membership")).Status);
        Assert.Equal(PaymentIntentResultStatus.UpgradeCheckoutNotReady, (await f.Service.CheckoutAsync(UserId, "Membership")).Status);
        Assert.Single(f.Orders.Items); Assert.Single(f.Orders.Histories);
        Assert.Equal(PaymentOrderStatus.Expired, pending.Status); Assert.Equal(end, p.EndsAt);
        Assert.Empty(f.Gateway.Requests); Assert.Equal(0, f.Gateway.LookupCalls);
    }

    [Fact]
    public async Task QuoteRequiresPersistedUser()
    {
        var f = new PaymentServiceTests.Fixture(persistedUser: false);
        Assert.Equal(PaymentIntentResultStatus.NonPersistedUser,
            (await f.Service.GetCheckoutQuoteAsync(UserId, "Membership")).Status);
    }

    [Fact]
    public async Task VietnamMidnightChangesRemainingDay()
    {
        var before = Now.Date.AddHours(16).AddMinutes(59);
        var f = new PaymentServiceTests.Fixture(now: before); AddNative(f, Now);
        var g = new PaymentServiceTests.Fixture(now: before.AddMinutes(1)); AddNative(g, Now);
        Assert.Equal(6, Assert.Single((await f.Service.GetCheckoutQuoteAsync(UserId, "Membership")).Response!.Credits).RemainingDays);
        Assert.Equal(5, Assert.Single((await g.Service.GetCheckoutQuoteAsync(UserId, "Membership")).Response!.Credits).RemainingDays);
    }

    [Fact]
    public async Task OrderLookupMetadataUsesPersistedSnapshot_NotCurrentVersion()
    {
        var f = Fixture();
        var o = new PaymentOrder { UserId = UserId, PlanId = SubscriptionBaseline.PlanId(PlanCode.TripPass),
            Amount = 10000, CreditAmount = 9000, Type = PaymentOrderType.Upgrade, Status = PaymentOrderStatus.Paid };
        f.Orders.Items.Add(o);
        var response = (await f.Service.GetOrderAsync(UserId, o.Id)).Response!;
        Assert.Equal("Upgrade", response.Type); Assert.Equal(19000, response.ListPrice); Assert.Equal(9000, response.CreditAmount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" Free ")]
    [InlineData("unknown")]
    public async Task InvalidOrFreeQuoteCannotCreateAnOrder(string? code)
    {
        var f = Fixture();
        Assert.Equal(PaymentIntentResultStatus.InvalidPlanCode,
            (await f.Service.GetCheckoutQuoteAsync(UserId, code)).Status);
        Assert.Empty(f.Orders.Items); Assert.Empty(f.Gateway.Requests);
    }

    [Theory]
    [InlineData(PlanCode.TripPass, "TripPass", PaymentIntentResultStatus.PlanAlreadyActive)]
    [InlineData(PlanCode.Membership, "TripPass", PaymentIntentResultStatus.CoveredByHigherPlan)]
    public async Task QuoteAndCheckoutShareSamePlanAndDowngradeErrors(
        PlanCode current, string target, PaymentIntentResultStatus expected)
    {
        var f = Fixture(); AddNative(f, code: current);
        Assert.Equal(expected, (await f.Service.GetCheckoutQuoteAsync(UserId, target)).Status);
        Assert.Equal(expected, (await f.Service.CheckoutAsync(UserId, target)).Status);
        Assert.Empty(f.Orders.Items); Assert.Empty(f.Orders.Histories); Assert.Empty(f.Gateway.Requests);
    }

    [Fact]
    public async Task ActiveNativeSourceWithNoRemainingCalendarDaysStillHasZeroRow()
    {
        var f = Fixture(); AddNative(f, Now.AddDays(-6));
        var q = (await f.Service.GetCheckoutQuoteAsync(UserId, "Membership")).Response!;
        var row = Assert.Single(q.Credits);
        Assert.Equal(0, row.RemainingDays); Assert.Equal(0, row.CreditAmount);
        Assert.Equal(59000, q.Amount);
    }

    [Fact]
    public void SourceSetExcludesExpiredTerminatedFreeAndHigher_KeptMappingIsOriginalPeriod()
    {
        var f = Fixture();
        var (_, expired) = AddNative(f, Now.AddDays(-7));
        var (_, terminated) = AddNative(f, Now.AddDays(-1));
        terminated.Terminate(Now, Guid.NewGuid());
        var (_, kept) = AddNative(f, Now.AddDays(3));
        var (_, higher) = AddNative(f, code: PlanCode.Membership);
        var sources = f.Subscriptions.Periods.Select(p => new SubscriptionQuoteSource(
            p, f.Subscriptions.Plans.Single(x => x.Id == p.PlanId),
            f.Subscriptions.Versions.Single(x => x.Id == p.PlanVersionId),
            f.Subscriptions.QuoteOrders.Single(x => x.Id == p.SourcePaymentOrderId))).ToList();
        var free = f.Subscriptions.Plans.Single(p => p.Code == "FREE");
        sources.Add(new(new() { UserId = UserId, PlanId = free.Id, StartsAt = Now, EndsAt = Now.AddDays(7) },
            free, null, null));
        var result = SubscriptionQuoteEvidence.Build(UserId, 200, Now, sources);
        Assert.Equal(kept.Id, Assert.Single(result).PeriodId);
        Assert.DoesNotContain(result, x => x.PeriodId == expired.Id || x.PeriodId == terminated.Id || x.PeriodId == higher.Id);
    }
}
