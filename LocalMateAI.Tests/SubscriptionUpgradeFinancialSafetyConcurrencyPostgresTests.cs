using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using static LocalMateAI.Tests.SubscriptionUpgradeReservationPostgresTests;

namespace LocalMateAI.Tests;

public sealed class SubscriptionUpgradeFinancialSafetyConcurrencyPostgresTests
{
    [Theory]
    [InlineData("C1")]
    [InlineData("C2")]
    [InlineData("C3")]
    [InlineData("C4")]
    [InlineData("C5")]
    [InlineData("C6")]
    [InlineData("C7")]
    [InlineData("C8")]
    [InlineData("C9")]
    [InlineData("C10")]
    public async Task FinancialRaceMatrix(string scenario)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync(); await using var seed = db.Context();
        var user = await Seed(seed);
        async Task<UpgradePreparationResult> Prepare(string code = "Membership")
        { await using var c = db.Context(); return await Service(c).PrepareAsync(user, code); }
        async Task<PaymentIntentResult> Ordinary(PaymentOrderType type)
        {
            await using var c = db.Context();
            var r = await new PaymentOperationExecutor(c).ExecuteForUserAsync(user, async ct =>
            {
                var v = await c.SubscriptionPlanVersions.SingleAsync(v => v.Id == SubscriptionBaseline.VersionId(PlanCode.TripPass), ct);
                var repository = new PaymentOrderRepository(c, Clock);
                if (await SubscriptionPendingOrderPolicy.CheckAsync(repository, user, v.PlanId, type, Now, ct) is { } blocker) return blocker;
                var order = new PaymentOrder { UserId = user, PlanId = v.PlanId, PlanVersionId = v.Id, PlanVersionBinding = PlanVersionBinding.Native,
                    Amount = v.Price, Type = type, ExpiresAt = Now.AddMinutes(15), CheckoutUrl = "https://checkout.test/order", QrCode = "test" };
                await repository.AddAsync(order, ct);
                return new PaymentIntentResult(PaymentIntentResultStatus.Success);
            });
            return r.Result!;
        }
        if (scenario is "C1" or "C2")
        {
            if (scenario == "C2") await PlanVersionFoundationPostgresTests.CustomAsync(seed, "SECOND_TARGET", 300, 90000, 30, null, null);
            var r = await Task.WhenAll(Prepare(), Prepare(scenario == "C2" ? "SECOND_TARGET" : "Membership"));
            Assert.Single(r, x => x.Status == PaymentIntentResultStatus.Success);
            Assert.Single(r, x => x.Status is PaymentIntentResultStatus.PendingOrderExists or PaymentIntentResultStatus.AnotherPendingOrder);
            Assert.Single(await seed.PaymentOrderCredits.ToListAsync());
        }
        else if (scenario == "C3")
        {
            var r = await Task.WhenAll(Ordinary(PaymentOrderType.Purchase), Ordinary(PaymentOrderType.Renewal));
            Assert.Single(r, x => x.Status == PaymentIntentResultStatus.Success);
            Assert.Single(r, x => x.Status == PaymentIntentResultStatus.AnotherPendingOrder);
        }
        else if (scenario == "C4")
        {
            var a = Ordinary(PaymentOrderType.Purchase); var b = Prepare();
            await Task.WhenAll(a, b);
            Assert.Equal(1, ((await a).Status == PaymentIntentResultStatus.Success ? 1 : 0) + ((await b).Status == PaymentIntentResultStatus.Success ? 1 : 0));
        }
        else if (scenario == "C5")
        {
            async Task Single()
            {
                await using var c = db.Context();
                var r = await SingleItineraryPostgresTests.Purchases(c, new SingleItineraryPostgresTests.Gateway())
                    .CheckoutAsync(user, Guid.NewGuid());
                Assert.NotNull(r.Response);
            }
            await Task.WhenAll(Single(), Ordinary(PaymentOrderType.Purchase));
            Assert.Equal(2, await seed.PaymentOrders.CountAsync(o => o.UserId == user && o.Status == PaymentOrderStatus.Pending));
            Assert.Single(await seed.PaymentOrders.Where(o => o.UserId == user && o.ProductKind == PaymentProductKind.SingleItinerary).ToListAsync());
        }
        else
        {
            var old = await Prepared(seed, user);
            if (scenario == "C6")
            {
                var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var gateway = new Gateway { Amount = old.Amount, Before = async () => { entered.SetResult(); await resume.Task; } };
                await using var releaseContext = db.Context();
                var release = Service(releaseContext, gateway).ResolveForReplacementAsync(user, old.Id);
                await entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
                Assert.Equal(PaymentIntentResultStatus.AnotherPendingOrder, (await Prepare()).Status);
                resume.SetResult(); Assert.Equal(UpgradeReleaseStatus.Released, (await release).Status);
                Assert.Equal(PaymentIntentResultStatus.Success, (await Prepare()).Status);
                Assert.Equal(2, await seed.PaymentOrderCredits.CountAsync());
                Assert.Equal(1, await seed.PaymentOrderCredits.CountAsync(c => c.ReleasedAt == null));
            }
            else if (scenario == "C7")
            {
                var gateway = new Gateway { Amount = old.Amount, Before = async () =>
                {
                    await using var paid = db.Context(); await using var tx = await paid.Database.BeginTransactionAsync();
                    await paid.Database.SqlQuery<int>($"""SELECT 1 AS "Value" FROM "Users" WHERE "Id"={user} FOR UPDATE""").SingleAsync();
                    var o = await paid.PaymentOrders.FromSqlInterpolated($"""SELECT * FROM "PaymentOrders" WHERE "Id"={old.Id} FOR UPDATE""").SingleAsync();
                    // Disposable-only simulation of the future shared Paid path; no production Upgrade settlement is enabled.
                    paid.SubscriptionPeriods.Add(new() { UserId = user, PlanId = o.PlanId!.Value, PlanVersionId = o.PlanVersionId!.Value,
                        SourcePaymentOrderId = o.Id, StartsAt = Now, EndsAt = Now.AddDays(30) });
                    o.Status = PaymentOrderStatus.Paid; o.PaidAt = Now;
                    await paid.SaveChangesAsync(); await tx.CommitAsync();
                } };
                await using var c = db.Context();
                Assert.Equal(UpgradeReleaseStatus.StaleEvidence, (await Service(c, gateway).ResolveForReplacementAsync(user, old.Id)).Status);
                Assert.Null((await seed.PaymentOrderCredits.AsNoTracking().SingleAsync()).ReleasedAt);
                Assert.Equal(PaymentIntentResultStatus.PlanAlreadyActive, (await Prepare()).Status);
            }
            else if (scenario == "C8")
            {
                var entered = 0; var both = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var gateway = new Gateway { Amount = old.Amount, Before = async () =>
                    { if (Interlocked.Increment(ref entered) == 2) both.SetResult(); await both.Task.WaitAsync(TimeSpan.FromSeconds(15)); } };
                async Task<UpgradeReleaseResult> Release()
                { await using var c = db.Context(); return await Service(c, gateway).ResolveForReplacementAsync(user, old.Id); }
                var r = await Task.WhenAll(Release(), Release());
                Assert.Single(r, x => x.Status == UpgradeReleaseStatus.Released);
                Assert.Single(r, x => x.Status == UpgradeReleaseStatus.AlreadyReleased);
                Assert.Equal(2, gateway.Calls); Assert.True((await seed.PaymentOrderCredits.AsNoTracking().SingleAsync()).HasValidReleaseEvidence());
            }
            else
            {
                var gateway = new Gateway(scenario == "C10" ? PaymentGatewayOrderStatus.Underpaid : PaymentGatewayOrderStatus.Unknown)
                { Amount = old.Amount };
                await using var c = db.Context();
                var release = Service(c, gateway).ResolveForReplacementAsync(user, old.Id);
                var prep = Prepare(); await Task.WhenAll(release, prep);
                Assert.Equal(UpgradeReleaseStatus.Blocked, (await release).Status);
                Assert.Equal(PaymentIntentResultStatus.AnotherPendingOrder, (await prep).Status);
                Assert.Null((await seed.PaymentOrderCredits.AsNoTracking().SingleAsync()).ReleasedAt);
            }
        }
        Assert.InRange(await seed.PaymentOrders.CountAsync(o => o.UserId == user
            && o.ProductKind == PaymentProductKind.SubscriptionPlan && o.Status == PaymentOrderStatus.Pending), 0, 1);
    }
}
