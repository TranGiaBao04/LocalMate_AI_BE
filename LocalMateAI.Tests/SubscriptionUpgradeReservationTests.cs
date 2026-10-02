using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

public sealed class SubscriptionUpgradeReservationTests
{
    private static readonly Guid User = SubscriptionCheckoutQuoteTests.UserId;
    private static readonly DateTime Now = SubscriptionCheckoutQuoteTests.Now;

    [Theory]
    [InlineData("native", 10857)]
    [InlineData("unproven", 0)]
    public async Task InternalPreparationRecomputesUnderBoundaryAndNeverCallsGateway(string provenance, int raw)
    {
        var f = new PaymentServiceTests.Fixture(now: Now);
        var (source, _) = SubscriptionCheckoutQuoteTests.AddNative(f);
        if (provenance == "unproven") source.PlanVersionBinding = PlanVersionBinding.LegacyVerified;
        var quote = (await f.Service.GetCheckoutQuoteAsync(User, "Membership")).Response!;
        var executor = new Executor
        {
            Before = () =>
            {
                var plan = f.Subscriptions.Plans.Single(p => p.Id == SubscriptionBaseline.PlanId(PlanCode.Membership));
                var next = new SubscriptionPlanVersion { PlanId = plan.Id, Price = 69000, DurationDays = 40 };
                f.Subscriptions.Versions.Add(next); plan.CurrentVersionId = next.Id;
            }
        };
        var credits = new Credits(executor);
        var service = new SubscriptionUpgradeReservationService(f.Subscriptions, f.Orders, credits, executor, f.Gateway,
            new PlanVersionFoundationPostgresTests.Clock(Now));
        var r = await service.PrepareAsync(User, "Membership");
        Assert.Equal(PaymentIntentResultStatus.Success, r.Status);
        var order = Assert.Single(f.Orders.Items);
        Assert.Equal(69000, order.Amount + order.CreditAmount); Assert.NotEqual(quote.Amount, order.Amount);
        Assert.Equal(raw, Assert.Single(credits.Rows).CalculatedCreditAmount);
        Assert.Equal(order.Id, credits.Rows[0].OrderId); Assert.Equal(User, credits.Rows[0].UserId);
        Assert.Equal(PaymentOrderType.Upgrade, order.Type); Assert.Null(order.CheckoutUrl);
        Assert.Empty(f.Gateway.Requests); Assert.Equal(0, f.Gateway.LookupCalls);
    }

    [Fact]
    public async Task MissingUserDoesNoFinancialWork()
    {
        var f = new PaymentServiceTests.Fixture(now: Now); var executor = new Executor { Exists = false };
        var credits = new Credits(executor);
        Assert.Equal(PaymentIntentResultStatus.NonPersistedUser,
            (await new SubscriptionUpgradeReservationService(f.Subscriptions, f.Orders, credits, executor, f.Gateway,
                new PlanVersionFoundationPostgresTests.Clock(Now)).PrepareAsync(User, "Membership")).Status);
        Assert.Empty(f.Orders.Items); Assert.Empty(credits.Rows); Assert.Empty(f.Gateway.Requests);
    }

    [Theory]
    [InlineData("proofless")]
    [InlineData("paid_money")]
    [InlineData("status")]
    [InlineData("remaining")]
    [InlineData("stale")]
    [InlineData("reproof")]
    [InlineData("preproof")]
    public async Task EfRejectsIncompleteOrRewrittenReleaseProof(string flaw)
    {
        using var c = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(
            "Host=localhost;Database=localmate_s1b_test_guard;Username=design", o => o.UseNetTopologySuite()).Options);
        var row = new PaymentOrderCredit { OrderId = Guid.NewGuid(), PeriodId = Guid.NewGuid(), OriginalEndsAt = Now };
        if (flaw == "preproof") { row.ReleaseProviderAmountPaid = 0; c.Add(row); }
        else
        {
            if (flaw == "reproof") row.Release(Now, new(Now, "Cancelled", 10000, 0, 10000, CreditReleaseEvidence.SafeReason));
            c.Attach(row);
            if (flaw == "proofless") row.ReleasedAt = Now;
            else if (flaw == "reproof") row.ReleaseReasonCode = "changed";
            else
            {
                row.Release(Now, new(Now, "Cancelled", 10000, 0, 10000, CreditReleaseEvidence.SafeReason));
                if (flaw == "paid_money") row.ReleaseProviderAmountPaid = 1;
                if (flaw == "status") row.ReleaseProviderStatus = "Expired";
                if (flaw == "remaining") row.ReleaseProviderAmountRemaining = 9999;
                if (flaw == "stale") row.ReleaseProviderCheckedAt = Now.AddMinutes(-2);
            }
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => c.SaveChangesAsync());
    }

    private sealed class Executor : IPaymentOperationExecutor
    {
        public bool Exists = true;
        public bool Inside;
        public Action? Before;
        public async Task<PaymentOperationExecution<T>> ExecuteForUserAsync<T>(Guid user, Func<CancellationToken, Task<T>> operation,
            CancellationToken ct = default)
        {
            if (!Exists) return new(false, default);
            Inside = true; Before?.Invoke();
            try { return new(true, await operation(ct)); } finally { Inside = false; }
        }
    }
    private sealed class Credits(Executor executor) : IPaymentCreditRepository
    {
        public List<PaymentOrderCredit> Rows = [];
        public Task AddClaimsAsync(IReadOnlyList<PaymentOrderCredit> claims, CancellationToken ct = default)
        { Assert.True(executor.Inside); Rows.AddRange(claims); return Task.CompletedTask; }
        public Task<UpgradeReleaseCandidate?> GetReleaseCandidateAsync(Guid user, Guid order, CancellationToken ct = default) =>
            Task.FromResult<UpgradeReleaseCandidate?>(null);
        public Task<UpgradeReleaseStatus> ReleaseAsync(UpgradeReleaseCandidate expected, CreditReleaseEvidence proof, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
