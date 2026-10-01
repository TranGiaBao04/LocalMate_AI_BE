using System.Net;
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

public sealed class PaymentReconciliationPostgresTests
{
    private static readonly DateTime Now = PaymentReconciliationTests.Now;
    private static readonly Guid Actor = PaymentReconciliationTests.Actor;

    [Theory]
    [InlineData(PaymentGatewayOrderStatus.Paid, PaymentOrderStatus.Pending, PaymentOrderStatus.Paid, PaymentStatusChangeSource.AdminReconcile)]
    [InlineData(PaymentGatewayOrderStatus.Paid, PaymentOrderStatus.Failed, PaymentOrderStatus.Paid, PaymentStatusChangeSource.AdminReconcile)]
    [InlineData(PaymentGatewayOrderStatus.Paid, PaymentOrderStatus.Expired, PaymentOrderStatus.Paid, PaymentStatusChangeSource.AdminReconcile)]
    [InlineData(PaymentGatewayOrderStatus.Paid, PaymentOrderStatus.Pending, PaymentOrderStatus.Paid, PaymentStatusChangeSource.BackgroundReconcile)]
    [InlineData(PaymentGatewayOrderStatus.Paid, PaymentOrderStatus.Pending, PaymentOrderStatus.Paid, PaymentStatusChangeSource.ProviderLookup)]
    [InlineData(PaymentGatewayOrderStatus.Cancelled, PaymentOrderStatus.Pending, PaymentOrderStatus.Failed, PaymentStatusChangeSource.AdminReconcile)]
    [InlineData(PaymentGatewayOrderStatus.Underpaid, PaymentOrderStatus.Pending, PaymentOrderStatus.Failed, PaymentStatusChangeSource.BackgroundReconcile)]
    [InlineData(PaymentGatewayOrderStatus.Failed, PaymentOrderStatus.Pending, PaymentOrderStatus.Failed, PaymentStatusChangeSource.AdminReconcile)]
    [InlineData(PaymentGatewayOrderStatus.Expired, PaymentOrderStatus.Pending, PaymentOrderStatus.Expired, PaymentStatusChangeSource.AdminReconcile)]
    [InlineData(PaymentGatewayOrderStatus.Pending, PaymentOrderStatus.Pending, PaymentOrderStatus.Expired, PaymentStatusChangeSource.BackgroundReconcile)]
    [InlineData(PaymentGatewayOrderStatus.Processing, PaymentOrderStatus.Pending, PaymentOrderStatus.Pending, PaymentStatusChangeSource.BackgroundReconcile)]
    [InlineData(PaymentGatewayOrderStatus.Unknown, PaymentOrderStatus.Pending, PaymentOrderStatus.Pending, PaymentStatusChangeSource.AdminReconcile)]
    public async Task Core_UsesRealSettlementAndHistory_ForAllSourcesAndProviderStates(PaymentGatewayOrderStatus provider,
        PaymentOrderStatus initial, PaymentOrderStatus expected, PaymentStatusChangeSource source)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var order = await Seed(c, initial);
        var count = await c.PaymentOrderStatusHistories.CountAsync();
        var gateway = new PaymentReconciliationTests.Gateway(provider) { BeforeLookup = _ => { Assert.Null(c.Database.CurrentTransaction); return Task.CompletedTask; } };
        var result = await Core(c, gateway).ReconcileAsync(order.Id, new(source, source == PaymentStatusChangeSource.BackgroundReconcile ? null : Actor));
        Assert.Equal(expected, result.LocalStatusAfter);
        Assert.Equal(initial, result.LocalStatusBefore);
        Assert.Equal(initial != expected, result.StatusChanged);
        Assert.Equal(expected == initial ? PaymentReconciliationStatus.NoChange : PaymentReconciliationStatus.Reconciled, result.Status);
        Assert.Equal(count + (initial != expected ? 1 : 0), await c.PaymentOrderStatusHistories.CountAsync());
        if (initial != expected)
        {
            var history = await c.PaymentOrderStatusHistories.SingleAsync(h => h.PaymentOrderId == order.Id && h.FromStatus == initial && h.ToStatus == expected);
            Assert.Equal(source, history.Source);
            Assert.Equal(source == PaymentStatusChangeSource.BackgroundReconcile ? (Guid?)null : Actor, history.ActorUserId);
            Assert.Null(history.WebhookReceiptId);
        }
        Assert.Equal(expected == PaymentOrderStatus.Paid ? 1 : 0, await c.SubscriptionPeriods.CountAsync());
        Assert.Equal(expected == PaymentOrderStatus.Paid ? 1 : 0, await c.EmailOutboxMessages.CountAsync());
        Assert.Empty(await c.PaymentWebhookReceipts.ToListAsync());
        Assert.False(c.Database.HasPendingModelChanges());
        var repeat = await Core(c, gateway).ReconcileAsync(order.Id, new(source));
        Assert.Equal(expected == PaymentOrderStatus.Paid ? PaymentReconciliationStatus.AlreadyPaid : PaymentReconciliationStatus.NoChange, repeat.Status);
        Assert.Equal(count + (initial != expected ? 1 : 0), await c.PaymentOrderStatusHistories.CountAsync());
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("timeout")]
    [InlineData("http")]
    [InlineData("mismatch")]
    public async Task Failure_DoesNotMutateFinancialFieldsOrExpire(string failure)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var order = await Seed(c);
        var before = await c.PaymentOrders.AsNoTracking().SingleAsync();
        var gateway = new PaymentReconciliationTests.Gateway(PaymentGatewayOrderStatus.Paid)
        { Failure = failure == "mismatch" ? null : failure, Mismatch = failure == "mismatch" };
        var result = await Core(c, gateway).ReconcileAsync(order.Id, new(PaymentStatusChangeSource.AdminReconcile, Actor));
        Assert.Equal(failure == "mismatch" ? PaymentReconciliationStatus.ProviderMismatch : PaymentReconciliationStatus.ProviderUnavailable, result.Status);
        var after = await c.PaymentOrders.AsNoTracking().SingleAsync();
        Assert.Equal(before.Status, after.Status);
        Assert.Equal(before.UpdatedAt, after.UpdatedAt);
        Assert.Equal(before.Amount, after.Amount);
        Assert.Equal(before.PlanVersionId, after.PlanVersionId);
        Assert.Equal(before.PaidAt, after.PaidAt);
        Assert.Single(await c.PaymentOrderStatusHistories.ToListAsync());
        Assert.Empty(await c.SubscriptionPeriods.ToListAsync());
        Assert.Empty(await c.EmailOutboxMessages.ToListAsync());
    }

    [Fact]
    public async Task AmountMismatch_UsesExistingFailurePath_NoGrant()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var order = await Seed(c, amount: 59000, plan: PlanCode.Membership);
        var result = await Core(c, new(PaymentGatewayOrderStatus.Paid)).ReconcileAsync(order.Id, new(PaymentStatusChangeSource.AdminReconcile, Actor));
        Assert.Equal(PaymentSettlementStatus.AmountMismatch, result.SettlementStatus);
        Assert.Equal(PaymentOrderStatus.Failed, result.LocalStatusAfter);
        var history = await c.PaymentOrderStatusHistories.SingleAsync(h => h.ToStatus == PaymentOrderStatus.Failed);
        Assert.Equal("amount_mismatch", history.ReasonCode);
        Assert.Equal(Actor, history.ActorUserId);
        Assert.Empty(await c.SubscriptionPeriods.ToListAsync());
        Assert.Empty(await c.EmailOutboxMessages.ToListAsync());
        Assert.Equal(59000m, (await c.PaymentOrders.AsNoTracking().SingleAsync()).Amount);
    }

    [Fact]
    public async Task AdminHttp_UsesRealCore_WritesAdminActor_AndAlreadyPaidSkipsProvider()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var order = await Seed(c);
        var gateway = new PaymentReconciliationTests.Gateway(PaymentGatewayOrderStatus.Paid);
        using var host = new AdminTransactionsHttpTests.Host(reconciliation: Core(c, gateway));
        host.Authenticate("manage");
        var path = $"/api/admin/transactions/{order.Id}/reconcile";
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsync(path, null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await host.Client.PostAsync(path, null)).StatusCode);
        Assert.Equal(1, gateway.Calls);
        var history = await c.PaymentOrderStatusHistories.SingleAsync(h => h.ToStatus == PaymentOrderStatus.Paid);
        Assert.Equal(PaymentStatusChangeSource.AdminReconcile, history.Source);
        Assert.Equal(Actor, history.ActorUserId);
    }

    [Fact]
    public async Task Candidates_QueryOnlyExpiredPending_BoundedStableOldestFirst()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var first = await Seed(c);
        var others = Enumerable.Range(1, 6).Select(i => new PaymentOrder
        {
            Id = Guid.Parse($"00000000-0000-0000-0000-{i:D12}"), UserId = first.UserId,
            ProviderOrderCode = first.ProviderOrderCode + i, Amount = 19000, PlanCode = PlanCode.TripPass,
            PlanVersionBinding = PlanVersionBinding.LegacyUnresolved,
            Status = i == 3 ? PaymentOrderStatus.Failed : i == 4 ? PaymentOrderStatus.Expired : i == 5 ? PaymentOrderStatus.Paid : PaymentOrderStatus.Pending,
            ExpiresAt = i == 6 ? Now.AddMinutes(1) : Now.AddMinutes(-5)
        }).ToArray();
        c.PaymentOrders.AddRange(others);
        await c.SaveChangesAsync();
        c.ChangeTracker.Clear();
        Assert.Equal(others.Take(2).Select(o => o.Id), await new PaymentOrderRepository(c).GetExpiredPendingIdsAsync(Now, 2));
        Assert.Equal(others.Take(2).Select(o => o.Id).Append(first.Id), await new PaymentOrderRepository(c).GetExpiredPendingIdsAsync(Now, 100));
        Assert.Empty(c.ChangeTracker.Entries());
    }

    [Fact]
    public async Task SessionLease_IsExclusive_ReleasesAndReacquires_WithoutLongTransaction()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var a = db.Context();
        await using var b = db.Context();
        var first = new PaymentReconciliationLeaseProvider(a, NullLogger<PaymentReconciliationLeaseProvider>.Instance);
        var second = new PaymentReconciliationLeaseProvider(b, NullLogger<PaymentReconciliationLeaseProvider>.Instance);
        var lease = await first.TryAcquireAsync();
        Assert.NotNull(lease);
        try
        {
            Assert.Null(await second.TryAcquireAsync());
            Assert.Null(a.Database.CurrentTransaction);
            var idle = await b.Database.SqlQuery<bool>($"""
                SELECT a.xact_start IS NULL AND a.state='idle' AS "Value"
                FROM pg_stat_activity a JOIN pg_locks l ON l.pid=a.pid
                WHERE l.locktype='advisory' AND l.database=(SELECT oid FROM pg_database WHERE datname=current_database())
                """).ToListAsync();
            Assert.Single(idle);
            Assert.True(idle[0]);
        }
        finally { await lease.DisposeAsync(); }
        await using var acquired = await second.TryAcquireAsync();
        Assert.NotNull(acquired);
    }

    [Theory]
    [InlineData(PaymentGatewayOrderStatus.Paid, null, PaymentOrderStatus.Paid)]
    [InlineData(PaymentGatewayOrderStatus.Pending, null, PaymentOrderStatus.Expired)]
    [InlineData(PaymentGatewayOrderStatus.Paid, "timeout", PaymentOrderStatus.Pending)]
    public async Task Worker_RealCore_TimeoutRetryAndTransitions(PaymentGatewayOrderStatus status, string? failure, PaymentOrderStatus expected)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        PaymentOrder order;
        await using (var c = db.Context()) order = await Seed(c);
        var gateway = new PaymentReconciliationTests.Gateway(status) { Failure = failure };
        using var services = Services(db, gateway);
        var worker = Worker(services);
        Assert.Equal(1, await worker.RunOnceAsync());
        await using var read = db.Context();
        Assert.Equal(expected, (await read.PaymentOrders.AsNoTracking().SingleAsync()).Status);
        if (failure is not null)
        {
            Assert.Single(await read.PaymentOrderStatusHistories.ToListAsync());
            gateway.Failure = null;
            Assert.Equal(1, await worker.RunOnceAsync());
            Assert.Equal(PaymentOrderStatus.Paid, (await read.PaymentOrders.AsNoTracking().SingleAsync()).Status);
        }
        var transitions = await read.PaymentOrderStatusHistories.Where(h => h.FromStatus != null).ToListAsync();
        var transition = Assert.Single(transitions);
        Assert.Equal(PaymentStatusChangeSource.BackgroundReconcile, transition.Source);
        Assert.Null(transition.ActorUserId);
        Assert.Equal(expected == PaymentOrderStatus.Expired ? 0 : 1, await read.SubscriptionPeriods.CountAsync());
        Assert.Equal(order.Id, transition.PaymentOrderId);
    }

    [Fact]
    public async Task TwoWorkerInstances_OnlyOneOwnsBatch_AndCancellationReleasesSession()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using (var c = db.Context()) await Seed(c);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gateway = new PaymentReconciliationTests.Gateway(PaymentGatewayOrderStatus.Paid)
        { BeforeLookup = async ct => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); } };
        using var services = Services(db, gateway);
        using var cancellation = new CancellationTokenSource();
        var first = Worker(services).RunOnceAsync(cancellation.Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(0, await Worker(services).RunOnceAsync());
            Assert.Equal(1, gateway.Calls);
        }
        finally { cancellation.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => first);
        await using var c2 = db.Context();
        await using var lease = await new PaymentReconciliationLeaseProvider(c2, NullLogger<PaymentReconciliationLeaseProvider>.Instance).TryAcquireAsync();
        Assert.NotNull(lease);
        Assert.Single(await c2.PaymentOrderStatusHistories.ToListAsync());
        Assert.Equal(PaymentOrderStatus.Pending, (await c2.PaymentOrders.AsNoTracking().SingleAsync()).Status);
    }

    [Theory]
    [InlineData("admin", "webhook")]
    [InlineData("worker", "webhook")]
    [InlineData("admin", "worker")]
    [InlineData("admin", "admin")]
    public async Task ConcurrentCallers_OneGrantPaidTransitionAndReceiptOutbox(string left, string right)
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        PaymentOrder order;
        await using (var setup = db.Context()) order = await Seed(setup);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var lookups = 0;
        var expectedReads = right == "webhook" ? 1 : 2;
        var gateway = new PaymentReconciliationTests.Gateway(PaymentGatewayOrderStatus.Paid)
        { BeforeLookup = async ct => { if (Interlocked.Increment(ref lookups) == expectedReads) entered.TrySetResult(); await release.Task.WaitAsync(ct); } };
        using var services = Services(db, gateway);
        async Task Run(string kind)
        {
            if (kind == "worker") { await Worker(services).RunOnceAsync(); return; }
            await using var scope = services.CreateAsyncScope();
            if (kind == "webhook")
            {
                var c = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var service = new PaymentWebhookService(gateway, scope.ServiceProvider.GetRequiredService<IPaymentSettlementService>(),
                    new PaymentEvidenceRepository(c), Options.Create(new PaymentEvidenceOptions()),
                    new PaymentEvidenceTestClock(Now), NullLogger<PaymentWebhookService>.Instance);
                Assert.Equal(PaymentWebhookStatus.Acknowledged, (await service.ProcessAsync("""{"test":"verified-by-fake"}""")).Status);
            }
            else await scope.ServiceProvider.GetRequiredService<IPaymentReconciliationService>()
                .ReconcileAsync(order.Id, new(PaymentStatusChangeSource.AdminReconcile, Actor));
        }
        var first = Run(left);
        var second = right == "webhook" ? Task.CompletedTask : Run(right);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (right == "webhook") second = Run(right);
        }
        finally { release.TrySetResult(); }
        await Task.WhenAll(first, second);
        await using var read = db.Context();
        Assert.Equal(PaymentOrderStatus.Paid, (await read.PaymentOrders.AsNoTracking().SingleAsync()).Status);
        Assert.Single(await read.SubscriptionPeriods.ToListAsync());
        Assert.Single(await read.EmailOutboxMessages.ToListAsync());
        var paid = Assert.Single(await read.PaymentOrderStatusHistories.Where(h => h.ToStatus == PaymentOrderStatus.Paid).ToListAsync());
        Assert.Equal(paid.Source == PaymentStatusChangeSource.AdminReconcile ? (Guid?)Actor : null, paid.ActorUserId);
        Assert.Equal(2, await read.PaymentOrderStatusHistories.CountAsync());
        Assert.Equal(right == "webhook" ? 1 : 0, await read.PaymentWebhookReceipts.CountAsync());
    }

    internal static async Task<PaymentOrder> Seed(AppDbContext c, PaymentOrderStatus status = PaymentOrderStatus.Pending,
        decimal amount = 19000, PlanCode plan = PlanCode.TripPass)
    {
        var user = new User { Id = Actor, FullName = "WP7 admin fixture", Email = "wp7@fixture.local", RoleId = await TestRoles.GetUserRoleIdAsync(c) };
        c.Users.Add(user);
        await c.SaveChangesAsync();
        var order = new PaymentOrder
        {
            UserId = user.Id, ProviderOrderCode = 987654, Amount = amount, PlanCode = plan,
            PlanId = SubscriptionBaseline.PlanId(plan), PlanVersionId = SubscriptionBaseline.VersionId(plan),
            PlanVersionBinding = PlanVersionBinding.Native, Status = PaymentOrderStatus.Pending, Type = PaymentOrderType.Purchase,
            ExpiresAt = Now.AddMinutes(-1), CheckoutUrl = "PRIVATE_CHECKOUT_SENTINEL", QrCode = "PRIVATE_QR_SENTINEL"
        };
        var repo = new PaymentOrderRepository(c, new PaymentEvidenceTestClock(Now));
        await repo.AddAsync(order);
        if (status != PaymentOrderStatus.Pending)
            await repo.TransitionStatusAsync(order, status, new(PaymentStatusChangeSource.LocalExpiration), Now, default);
        c.ChangeTracker.Clear();
        return order;
    }

    private static PaymentReconciliationService Core(AppDbContext c, PaymentReconciliationTests.Gateway gateway) =>
        new(new PaymentOrderRepository(c, new PaymentEvidenceTestClock(Now)), gateway,
            new PaymentSettlementService(new PaymentSettlementExecutor(c, new PaymentEvidenceTestClock(Now)),
                new SubscriptionRepository(c), new UserRepository(c), new EmailOutboxRepository(c), new PaymentEvidenceTestClock(Now),
                NullLogger<PaymentSettlementService>.Instance), new PaymentEvidenceTestClock(Now), NullLogger<PaymentReconciliationService>.Instance);

    private static ServiceProvider Services(IsolatedPlanDatabase db, PaymentReconciliationTests.Gateway gateway)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(new PaymentEvidenceTestClock(Now));
        services.AddSingleton<IPaymentGateway>(gateway);
        services.AddDbContext<AppDbContext>(o => o.UseNpgsql(db.Connection, pg => pg.UseNetTopologySuite()));
        services.AddScoped<IPaymentOrderRepository, PaymentOrderRepository>();
        services.AddScoped<IPaymentReconciliationLeaseProvider, PaymentReconciliationLeaseProvider>();
        services.AddScoped<IPaymentReconciliationService, PaymentReconciliationService>();
        services.AddScoped<IPaymentSettlementService, PaymentSettlementService>();
        services.AddScoped<IPaymentSettlementExecutor, PaymentSettlementExecutor>();
        services.AddScoped<ISubscriptionRepository, SubscriptionRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IEmailOutboxRepository, EmailOutboxRepository>();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }

    private static PaymentReconciliationWorker Worker(ServiceProvider services) => new(
        services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new PaymentReconciliationOptions()),
        new PaymentEvidenceTestClock(Now), NullLogger<PaymentReconciliationWorker>.Instance);
}
