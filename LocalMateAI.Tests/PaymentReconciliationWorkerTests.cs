using LocalMateAI.API.BackgroundJobs;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Enums;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Tests;

public sealed class PaymentReconciliationWorkerTests
{
    internal static ServiceProvider Services(PaymentReconciliationTests.MemoryOrders orders,
        PaymentReconciliationTests.Gateway gateway, IPaymentReconciliationLeaseProvider lease)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPaymentOrderRepository>(orders);
        services.AddSingleton(lease);
        services.AddScoped<IPaymentReconciliationService>(_ => PaymentReconciliationTests.Core(orders, gateway));
        return services.BuildServiceProvider();
    }

    internal static PaymentReconciliationWorker Worker(ServiceProvider services, int batch = 100) =>
        new(services.GetRequiredService<IServiceScopeFactory>(), Options.Create(new PaymentReconciliationOptions { BatchSize = batch }),
            new PaymentEvidenceTestClock(PaymentReconciliationTests.Now), NullLogger<PaymentReconciliationWorker>.Instance);

    [Fact]
    public async Task Candidates_AreBoundedExpiredPending_OldestThenId_AndUseBackgroundContext()
    {
        var orders = new[] { PaymentReconciliationTests.Order(), PaymentReconciliationTests.Order(),
            PaymentReconciliationTests.Order(expired: false), PaymentReconciliationTests.Order(PaymentOrderStatus.Paid),
            PaymentReconciliationTests.Order(PaymentOrderStatus.Failed), PaymentReconciliationTests.Order(PaymentOrderStatus.Expired) };
        orders[0].ExpiresAt = PaymentReconciliationTests.Now.AddMinutes(-5);
        orders[1].ExpiresAt = PaymentReconciliationTests.Now.AddMinutes(-10);
        var repo = new PaymentReconciliationTests.MemoryOrders(orders);
        var lease = new LeaseProvider();
        using var services = Services(repo, new(PaymentGatewayOrderStatus.Pending), lease);
        Assert.Equal(1, await Worker(services, 1).RunOnceAsync());
        Assert.Equal(PaymentOrderStatus.Pending, orders[0].Status);
        Assert.Equal(PaymentOrderStatus.Expired, orders[1].Status);
        var history = Assert.Single(repo.History);
        Assert.Equal(PaymentStatusChangeSource.BackgroundReconcile, history.Source);
        Assert.Null(history.ActorUserId);
        Assert.Equal(orders[1].Id, history.PaymentOrderId);
        Assert.Equal(1, lease.Released);
    }

    [Theory]
    [InlineData(PaymentGatewayOrderStatus.Paid, null, PaymentOrderStatus.Paid)]
    [InlineData(PaymentGatewayOrderStatus.Pending, null, PaymentOrderStatus.Expired)]
    [InlineData(PaymentGatewayOrderStatus.Paid, "timeout", PaymentOrderStatus.Pending)]
    [InlineData(PaymentGatewayOrderStatus.Processing, null, PaymentOrderStatus.Pending)]
    public async Task Worker_UsesSameCore_NoTimeoutExpiry(PaymentGatewayOrderStatus provider, string? failure, PaymentOrderStatus expected)
    {
        var order = PaymentReconciliationTests.Order();
        var repo = new PaymentReconciliationTests.MemoryOrders(order);
        using var services = Services(repo, new(provider) { Failure = failure }, new LeaseProvider());
        Assert.Equal(1, await Worker(services).RunOnceAsync());
        Assert.Equal(expected, order.Status);
        if (expected == PaymentOrderStatus.Pending) Assert.Empty(repo.History);
    }

    [Fact]
    public async Task Unavailable_RetriesNextCycle_WithoutFakeHistory()
    {
        var order = PaymentReconciliationTests.Order();
        var repo = new PaymentReconciliationTests.MemoryOrders(order);
        var gateway = new PaymentReconciliationTests.Gateway(PaymentGatewayOrderStatus.Paid) { Failure = "unavailable" };
        using var services = Services(repo, gateway, new LeaseProvider());
        var worker = Worker(services);
        await worker.RunOnceAsync();
        Assert.Empty(repo.History);
        gateway.Failure = null;
        await worker.RunOnceAsync();
        Assert.Equal(2, gateway.Calls);
        Assert.Equal(PaymentOrderStatus.Paid, order.Status);
        Assert.Single(repo.History);
        Assert.Equal(0, await worker.RunOnceAsync());
    }

    [Fact]
    public async Task SecondWorker_SkipsEntireBatch_WhileFirstOwnsLease()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repo = new PaymentReconciliationTests.MemoryOrders(PaymentReconciliationTests.Order());
        var gateway = new PaymentReconciliationTests.Gateway(PaymentGatewayOrderStatus.Paid)
        { BeforeLookup = async ct => { entered.TrySetResult(); await release.Task.WaitAsync(ct); } };
        var lease = new LeaseProvider();
        using var services = Services(repo, gateway, lease);
        var first = Worker(services).RunOnceAsync();
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, await Worker(services).RunOnceAsync());
            Assert.Equal(1, repo.CandidateReads);
        }
        finally { release.TrySetResult(); }
        Assert.Equal(1, await first);
        Assert.Equal(1, gateway.Calls);
        Assert.False(lease.Held);
    }

    [Fact]
    public async Task Cancellation_StopsCandidateAndReleasesLease()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var repo = new PaymentReconciliationTests.MemoryOrders(PaymentReconciliationTests.Order());
        var gateway = new PaymentReconciliationTests.Gateway(PaymentGatewayOrderStatus.Paid)
        { BeforeLookup = async ct => { entered.TrySetResult(); await Task.Delay(Timeout.Infinite, ct); } };
        var lease = new LeaseProvider();
        using var services = Services(repo, gateway, lease);
        using var cancellation = new CancellationTokenSource();
        var running = Worker(services).RunOnceAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.False(lease.Held);
        Assert.Equal(1, lease.Released);
        Assert.Empty(repo.History);
    }

    [Theory]
    [InlineData(0, 60)]
    [InlineData(1001, 60)]
    [InlineData(100, 0)]
    [InlineData(100, 3601)]
    public void InvalidOptions_AreRejected(int batch, int interval) =>
        Assert.True(new PaymentReconciliationOptionsValidator().Validate(null,
            new() { BatchSize = batch, PollIntervalSeconds = interval }).Failed);

    [Fact]
    public void DefaultOptions_Are100And60_AndValidate()
    {
        var options = new PaymentReconciliationOptions();
        Assert.Equal(100, options.BatchSize);
        Assert.Equal(60, options.PollIntervalSeconds);
        Assert.True(new PaymentReconciliationOptionsValidator().Validate(null, options).Succeeded);
    }

    internal sealed class LeaseProvider : IPaymentReconciliationLeaseProvider
    {
        private int held;
        public bool Held => held != 0;
        public int Released { get; private set; }
        public Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult<IAsyncDisposable?>(Interlocked.CompareExchange(ref held, 1, 0) == 0 ? new Lease(this) : null); }
        private sealed class Lease(LeaseProvider owner) : IAsyncDisposable
        {
            public ValueTask DisposeAsync() { owner.Released++; Interlocked.Exchange(ref owner.held, 0); return ValueTask.CompletedTask; }
        }
    }
}
