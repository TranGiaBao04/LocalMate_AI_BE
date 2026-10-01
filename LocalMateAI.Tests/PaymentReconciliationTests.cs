using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMateAI.Tests;

public sealed class PaymentReconciliationTests
{
    internal static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    internal static readonly Guid Actor = Guid.Parse("00000000-0000-0000-0000-000000000003");
    internal static PaymentOrder Order(PaymentOrderStatus status = PaymentOrderStatus.Pending, bool expired = true) => new()
    {
        UserId = Actor, ProviderOrderCode = 987654, Amount = 19000, Status = status,
        ExpiresAt = expired ? Now : Now.AddMinutes(5), CheckoutUrl = "PRIVATE_CHECKOUT", QrCode = "PRIVATE_QR"
    };
    internal static PaymentReconciliationService Core(MemoryOrders orders, Gateway gateway, Settlement? settlement = null) =>
        new(orders, gateway, settlement ?? new(orders), new PaymentEvidenceTestClock(Now), NullLogger<PaymentReconciliationService>.Instance);

    [Theory]
    [InlineData(PaymentGatewayOrderStatus.Paid, PaymentOrderStatus.Paid, "provider_paid")]
    [InlineData(PaymentGatewayOrderStatus.Cancelled, PaymentOrderStatus.Failed, "provider_cancelled")]
    [InlineData(PaymentGatewayOrderStatus.Underpaid, PaymentOrderStatus.Failed, "provider_underpaid")]
    [InlineData(PaymentGatewayOrderStatus.Failed, PaymentOrderStatus.Failed, "provider_failed")]
    [InlineData(PaymentGatewayOrderStatus.Expired, PaymentOrderStatus.Expired, "provider_expired")]
    [InlineData(PaymentGatewayOrderStatus.Pending, PaymentOrderStatus.Expired, "local_expired")]
    public async Task OneMapping_UsesExistingSettlementOrAtomicExpiry(PaymentGatewayOrderStatus provider,
        PaymentOrderStatus expected, string reason)
    {
        var order = Order();
        var orders = new MemoryOrders(order);
        var gateway = new Gateway(provider);
        var settlement = new Settlement(orders);
        var result = await Core(orders, gateway, settlement).ReconcileAsync(order.Id, new(PaymentStatusChangeSource.AdminReconcile, Actor));
        Assert.Equal(PaymentReconciliationStatus.Reconciled, result.Status);
        Assert.Equal(PaymentOrderStatus.Pending, result.LocalStatusBefore);
        Assert.Equal(expected, result.LocalStatusAfter);
        Assert.True(result.StatusChanged);
        Assert.True(result.ProviderChecked);
        Assert.Equal(provider, result.ProviderStatus);
        Assert.Equal(Now, result.CheckedAt);
        var history = Assert.Single(orders.History);
        Assert.Equal(reason, history.ReasonCode);
        Assert.Equal(PaymentStatusChangeSource.AdminReconcile, history.Source);
        Assert.Equal(Actor, history.ActorUserId);
        Assert.Null(history.WebhookReceiptId);
        Assert.Equal(provider is PaymentGatewayOrderStatus.Expired or PaymentGatewayOrderStatus.Pending ? 0 : 1, settlement.Calls);
    }

    [Theory]
    [InlineData(PaymentGatewayOrderStatus.Pending, PaymentOrderStatus.Pending, false)]
    [InlineData(PaymentGatewayOrderStatus.Pending, PaymentOrderStatus.Failed, true)]
    [InlineData(PaymentGatewayOrderStatus.Pending, PaymentOrderStatus.Expired, true)]
    [InlineData(PaymentGatewayOrderStatus.Expired, PaymentOrderStatus.Failed, true)]
    [InlineData(PaymentGatewayOrderStatus.Expired, PaymentOrderStatus.Expired, true)]
    [InlineData(PaymentGatewayOrderStatus.Processing, PaymentOrderStatus.Pending, true)]
    [InlineData(PaymentGatewayOrderStatus.Unknown, PaymentOrderStatus.Pending, true)]
    [InlineData(PaymentGatewayOrderStatus.Failed, PaymentOrderStatus.Failed, true)]
    public async Task NoChange_NeverInventsPendingOrHistory(PaymentGatewayOrderStatus provider, PaymentOrderStatus local, bool expired)
    {
        var order = Order(local, expired);
        var orders = new MemoryOrders(order);
        var result = await Core(orders, new(provider)).ReconcileAsync(order.Id, new(PaymentStatusChangeSource.BackgroundReconcile));
        Assert.Equal(PaymentReconciliationStatus.NoChange, result.Status);
        Assert.Equal(local, result.LocalStatusAfter);
        Assert.False(result.StatusChanged);
        Assert.Empty(orders.History);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("timeout")]
    [InlineData("gateway")]
    [InlineData("http")]
    [InlineData("cancelled_timeout")]
    [InlineData("exception")]
    public async Task ProviderFailures_DoNotExpireOrMutate(string failure)
    {
        var order = Order();
        var orders = new MemoryOrders(order);
        var gateway = new Gateway(PaymentGatewayOrderStatus.Paid) { Failure = failure };
        var result = await Core(orders, gateway).ReconcileAsync(order.Id, new(PaymentStatusChangeSource.BackgroundReconcile));
        Assert.Equal(PaymentReconciliationStatus.ProviderUnavailable, result.Status);
        Assert.True(result.ProviderChecked);
        Assert.Null(result.ProviderStatus);
        Assert.Equal(PaymentOrderStatus.Pending, order.Status);
        Assert.Empty(orders.History);
    }

    [Theory]
    [InlineData(PaymentOrderStatus.Failed)]
    [InlineData(PaymentOrderStatus.Expired)]
    public async Task LatePaid_UsesSettlement(PaymentOrderStatus status)
    {
        var order = Order(status);
        var orders = new MemoryOrders(order);
        Assert.Equal(PaymentReconciliationStatus.Reconciled,
            (await Core(orders, new(PaymentGatewayOrderStatus.Paid)).ReconcileAsync(order.Id,
                new(PaymentStatusChangeSource.AdminReconcile, Actor))).Status);
        Assert.Equal(status, Assert.Single(orders.History).FromStatus);
        Assert.Equal(PaymentOrderStatus.Paid, order.Status);
    }

    [Fact]
    public async Task LocalPaid_AndMissing_SkipProvider()
    {
        var order = Order(PaymentOrderStatus.Paid);
        var gateway = new Gateway(PaymentGatewayOrderStatus.Failed);
        var orders = new MemoryOrders(order);
        var core = Core(orders, gateway);
        var paid = await core.ReconcileAsync(order.Id, new(PaymentStatusChangeSource.AdminReconcile, Actor));
        Assert.Equal(PaymentReconciliationStatus.AlreadyPaid, paid.Status);
        Assert.False(paid.ProviderChecked);
        Assert.False(paid.StatusChanged);
        var missing = await core.ReconcileAsync(Guid.NewGuid(), new(PaymentStatusChangeSource.AdminReconcile, Actor));
        Assert.Equal(PaymentReconciliationStatus.NotFound, missing.Status);
        Assert.Null(missing.ProviderOrderCode);
        Assert.Equal(0, gateway.Calls);
        Assert.Empty(orders.History);
    }

    [Fact]
    public async Task MismatchedProviderCode_FailsClosed()
    {
        var order = Order();
        var orders = new MemoryOrders(order);
        var result = await Core(orders, new(PaymentGatewayOrderStatus.Paid) { Mismatch = true })
            .ReconcileAsync(order.Id, new(PaymentStatusChangeSource.AdminReconcile, Actor));
        Assert.Equal(PaymentReconciliationStatus.ProviderMismatch, result.Status);
        Assert.Null(result.ProviderStatus);
        Assert.False(result.StatusChanged);
        Assert.Empty(orders.History);
    }

    [Fact]
    public async Task RequestCancellation_IsNotReportedAsProviderUnavailable()
    {
        var order = Order();
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => Core(new(order), new(PaymentGatewayOrderStatus.Paid))
            .ReconcileAsync(order.Id, new(PaymentStatusChangeSource.AdminReconcile, Actor), cancelled.Token));
        Assert.Equal(PaymentOrderStatus.Pending, order.Status);
    }

    internal sealed class MemoryOrders(params PaymentOrder[] orders) : IPaymentOrderRepository
    {
        public List<PaymentOrder> Items { get; } = [.. orders];
        public List<PaymentOrderStatusHistory> History { get; } = [];
        public int CandidateReads { get; private set; }
        public Task<PaymentOrder?> GetByIdAsync(Guid id, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult(Items.SingleOrDefault(o => o.Id == id)); }
        public Task<PaymentOrder?> GetOwnedByIdAsync(Guid id, Guid user, CancellationToken ct = default) =>
            Task.FromResult(Items.SingleOrDefault(o => o.Id == id && o.UserId == user));
        public Task<PaymentOrder?> GetPendingAsync(Guid user, Guid plan, PaymentOrderType type, CancellationToken ct = default) =>
            Task.FromResult(Items.FirstOrDefault(o => o.UserId == user && o.PlanId == plan && o.Type == type && o.Status == PaymentOrderStatus.Pending));
        public Task<IReadOnlyList<Guid>> GetExpiredPendingIdsAsync(DateTime now, int size, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); CandidateReads++; return Task.FromResult<IReadOnlyList<Guid>>(Items.Where(o => o.Status == PaymentOrderStatus.Pending && o.ExpiresAt <= now)
            .OrderBy(o => o.ExpiresAt).ThenBy(o => o.Id).Take(size).Select(o => o.Id).ToArray()); }
        public Task AddAsync(PaymentOrder order, CancellationToken ct = default) { Items.Add(order); return Task.CompletedTask; }
        public Task SaveChangesAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task TransitionStatusAsync(PaymentOrder order, PaymentOrderStatus status, PaymentTransitionContext context, DateTime now, CancellationToken ct = default)
        { if (order.Status != status) { var before = order.Status; order.Status = status; History.Add(context.History(order, before, now)); } return Task.CompletedTask; }
        public Task<bool> MarkExpiredIfPendingAsync(Guid id, DateTime now, CancellationToken ct = default) =>
            MarkExpiredIfPendingAsync(id, now, new(PaymentStatusChangeSource.LocalExpiration), ct);
        public async Task<bool> MarkExpiredIfPendingAsync(Guid id, DateTime now, PaymentTransitionContext context, CancellationToken ct = default)
        { var order = Items.Single(o => o.Id == id); if (order.Status != PaymentOrderStatus.Pending) return false;
            await TransitionStatusAsync(order, PaymentOrderStatus.Expired, context, now, ct); return true; }
    }

    internal sealed class Gateway(PaymentGatewayOrderStatus status) : IPaymentGateway
    {
        public int Calls { get; private set; }
        public string? Failure { get; set; }
        public bool Mismatch { get; init; }
        public Func<CancellationToken, Task>? BeforeLookup { get; init; }
        public Task<PaymentLinkResult> CreatePaymentLinkAsync(PaymentLinkRequest request, CancellationToken ct = default) =>
            Task.FromResult(PaymentLinkResult.Unavailable());
        public Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(string raw, CancellationToken ct = default) =>
            Task.FromResult(PaymentWebhookVerificationResult.Valid(new(987654, 19000, true)));
        public async Task<PaymentGatewayOrderResult> GetPaymentAsync(long code, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Calls++;
            if (BeforeLookup is not null) await BeforeLookup(ct);
            if (Failure is not null && Failure != "unavailable") throw Failure switch
            { "timeout" => new TimeoutException(), "gateway" => new PaymentGatewayUnavailableException("test"),
              "http" => new HttpRequestException("test"), "cancelled_timeout" => new OperationCanceledException(),
              _ => new InvalidOperationException("PRIVATE_PROVIDER_SENTINEL") };
            return Failure == "unavailable" ? PaymentGatewayOrderResult.Unavailable(code)
                : new(true, Mismatch ? code + 1 : code, 19000, status);
        }
    }

    internal sealed class Settlement(MemoryOrders orders) : IPaymentSettlementService
    {
        public int Calls { get; private set; }
        public Task<PaymentSettlementResult> ApplyVerifiedPaymentAsync(VerifiedPaymentNotification notice, CancellationToken ct = default) =>
            ApplyVerifiedPaymentAsync(notice, new(PaymentStatusChangeSource.ProviderLookup), ct);
        public async Task<PaymentSettlementResult> ApplyVerifiedPaymentAsync(VerifiedPaymentNotification notice, PaymentTransitionContext context, CancellationToken ct = default)
        {
            Calls++;
            var order = orders.Items.Single(o => o.ProviderOrderCode == notice.ProviderOrderCode);
            if (order.Status == PaymentOrderStatus.Paid) return new(PaymentSettlementStatus.AlreadyPaid);
            var success = notice.IsSuccessful && notice.Amount == order.Amount;
            await orders.TransitionStatusAsync(order, success ? PaymentOrderStatus.Paid : PaymentOrderStatus.Failed, context, Now, ct);
            return new(success ? PaymentSettlementStatus.Settled : PaymentSettlementStatus.NonSuccessful);
        }
    }
}
