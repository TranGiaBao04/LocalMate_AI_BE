using System.Text.Json;
using LocalMateAI.API.BackgroundJobs;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Payments;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Tests;

public sealed class EntitlementRepairConcurrencyPostgresTests
{
    private static readonly DateTime Day = EntitlementRepairTests.Day;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentRepairs_SameOrDistinctOrders_SerializeWithoutExtension(bool distinct)
    {
        await using var f = await EntitlementRepairPostgresFixture.Create(distinct ? 2 : 1, distinct ? [0, 1] : [0]);
        await using var first = f.Context(); await using var second = f.Context();
        var results = await Task.WhenAll(
            EntitlementRepairPostgresFixture.Repair(first).RepairAsync(f.Orders[0].Id, f.Actor, "first attempt"),
            EntitlementRepairPostgresFixture.Repair(second).RepairAsync(f.Orders[distinct ? 1 : 0].Id, f.Actor, "second attempt"));
        Assert.Equal(distinct ? 2 : 1, results.Count(r => r.Response!.Result == EntitlementRepairOutcome.Repaired));
        Assert.Equal(distinct ? 0 : 1, results.Count(r => r.Response!.Result == EntitlementRepairOutcome.AlreadyGranted));
        await using var c = f.Context();
        var periods = await c.SubscriptionPeriods.AsNoTracking().OrderBy(p => p.StartsAt).ToArrayAsync();
        Assert.Equal(distinct ? 2 : 1, periods.Length);
        foreach (var period in periods)
        {
            var original = f.Original.Single(p => p.SourcePaymentOrderId == period.SourcePaymentOrderId);
            Assert.Equal(original.StartsAt, period.StartsAt); Assert.Equal(original.EndsAt, period.EndsAt);
        }
        Assert.Equal(distinct ? 2 : 1, await c.EntitlementRepairAudits.CountAsync(a => a.Outcome == EntitlementRepairOutcome.Repaired));
        Assert.Equal(2, await c.EntitlementRepairAudits.CountAsync());
        Assert.Equal(distinct ? 2 : 1, await c.PaymentOrderStatusHistories.CountAsync(h => h.ToStatus == PaymentOrderStatus.Paid));
        Assert.Equal(distinct ? 2 : 1, await c.EmailOutboxMessages.CountAsync());
    }

    [Theory]
    [InlineData("webhook")]
    [InlineData("admin")]
    [InlineData("background_core")]
    public async Task RepairVersusTerminalPaidSettlementCallers_OneGrant_NoNewPaidHistoryOrOutbox(string caller)
    {
        await using var f = await EntitlementRepairPostgresFixture.Create();
        await using var repair = f.Context(); await using var other = f.Context();
        var gateway = new PaymentEvidenceTestGateway
        {
            Verification = PaymentWebhookVerificationResult.Valid(new(f.Orders[0].ProviderOrderCode, 19000, true))
        };
        var settlement = EntitlementRepairPostgresFixture.Settlement(other, Day.AddDays(90));
        Task otherTask;
        Task<PaymentReconciliationResult>? reconcileTask = null;
        if (caller == "webhook")
            otherTask = new PaymentWebhookService(gateway, settlement, new PaymentEvidenceRepository(other),
                Options.Create(new PaymentEvidenceOptions()), new PaymentEvidenceTestClock(Day.AddDays(90)),
                NullLogger<PaymentWebhookService>.Instance).ProcessAsync("{\"verified\":true}");
        else
            otherTask = reconcileTask = new PaymentReconciliationService(new PaymentOrderRepository(other), gateway, settlement,
                new PaymentEvidenceTestClock(Day.AddDays(90)), NullLogger<PaymentReconciliationService>.Instance)
                .ReconcileAsync(f.Orders[0].Id, new(caller == "admin" ? PaymentStatusChangeSource.AdminReconcile
                    : PaymentStatusChangeSource.BackgroundReconcile, caller == "admin" ? f.Actor : null));
        var repairTask = EntitlementRepairPostgresFixture.Repair(repair).RepairAsync(f.Orders[0].Id, f.Actor, "repair race");
        await Task.WhenAll(otherTask, repairTask);
        Assert.Equal(EntitlementRepairOutcome.Repaired, (await repairTask).Response!.Result);
        if (reconcileTask is not null)
        {
            var reconciled = await reconcileTask;
            Assert.Equal(PaymentReconciliationStatus.AlreadyPaid, reconciled.Status);
            Assert.False(reconciled.ProviderChecked);
        }
        await using var verify = f.Context();
        Assert.Single(await verify.SubscriptionPeriods.ToListAsync());
        Assert.Single(await verify.EntitlementRepairAudits.ToListAsync());
        Assert.Single(await verify.PaymentOrderStatusHistories.Where(h => h.ToStatus == PaymentOrderStatus.Paid).ToListAsync());
        Assert.Single(await verify.EmailOutboxMessages.ToListAsync());
        Assert.Equal(caller == "webhook" ? 1 : 0, await verify.PaymentWebhookReceipts.CountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepairVersusAnotherSameUserPayment_PreservesHistoricalWindow(bool worker)
    {
        await using var f = await EntitlementRepairPostgresFixture.Create(2, [0]);
        await using (var setup = f.Context())
        {
            setup.PaymentOrders.Add(new PaymentOrder { UserId = f.Orders[0].UserId, PlanId = f.Orders[0].PlanId,
                PlanVersionId = f.Orders[0].PlanVersionId, PlanVersionBinding = PlanVersionBinding.Native,
                PlanCode = PlanCode.TripPass, Amount = 19000, Status = PaymentOrderStatus.Pending,
                ProviderOrderCode = 900001, ExpiresAt = Day.AddDays(2), Type = PaymentOrderType.Renewal });
            await setup.SaveChangesAsync();
        }
        var gateway = new PaymentEvidenceTestGateway { Lookup = new(true, 900001, 19000, PaymentGatewayOrderStatus.Paid) };
        var services = new ServiceCollection();
        services.AddLogging(); services.AddSingleton<TimeProvider>(new PaymentEvidenceTestClock(Day.AddDays(3)));
        services.AddSingleton<IPaymentGateway>(gateway);
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(f.Database.Connection, pg => pg.UseNetTopologySuite()));
        services.AddScoped<IPaymentOrderRepository, PaymentOrderRepository>();
        services.AddScoped<IPaymentReconciliationLeaseProvider, PaymentReconciliationLeaseProvider>();
        services.AddScoped<IPaymentReconciliationService, PaymentReconciliationService>();
        services.AddScoped<IPaymentSettlementService, PaymentSettlementService>();
        services.AddScoped<IPaymentSettlementExecutor, PaymentSettlementExecutor>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<IUserRepository, UserRepository>(); services.AddScoped<IEmailOutboxRepository, EmailOutboxRepository>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var repair = f.Context();
        var repairTask = EntitlementRepairPostgresFixture.Repair(repair).RepairAsync(f.Orders[0].Id, f.Actor, "repair while renewing");
        if (worker)
        {
            var job = new PaymentReconciliationWorker(provider.GetRequiredService<IServiceScopeFactory>(),
                Options.Create(new PaymentReconciliationOptions()), new PaymentEvidenceTestClock(Day.AddDays(3)),
                NullLogger<PaymentReconciliationWorker>.Instance);
            Assert.Equal(1, await job.RunOnceAsync());
        }
        else
        {
            await using var settle = f.Context();
            Assert.Equal(PaymentSettlementStatus.Settled, (await EntitlementRepairPostgresFixture.Settlement(settle, Day.AddDays(3))
                .ApplyVerifiedPaymentAsync(new(900001, 19000, true))).Status);
        }
        Assert.Equal(EntitlementRepairOutcome.Repaired, (await repairTask).Response!.Result);
        await using var c = f.Context();
        var periods = await c.SubscriptionPeriods.OrderBy(p => p.StartsAt).ToArrayAsync();
        Assert.Equal(3, periods.Length);
        Assert.Equal(Day, periods[0].StartsAt);
        Assert.Equal(Day.AddDays(7), periods[0].EndsAt);
        Assert.Equal(Day.AddDays(14), periods[2].StartsAt);
        Assert.Equal(Day.AddDays(21), periods[2].EndsAt);
        Assert.Equal(3, await c.PaymentOrderStatusHistories.CountAsync(h => h.ToStatus == PaymentOrderStatus.Paid));
        Assert.Equal(3, await c.EmailOutboxMessages.CountAsync());
    }
}
