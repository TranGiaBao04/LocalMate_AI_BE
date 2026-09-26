using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMateAI.Tests;

public sealed class PaymentServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Free")]
    [InlineData("Unknown")]
    [InlineData("membership")]
    public async Task Checkout_InvalidOrFreePlan_IsRejected(string? planCode)
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CheckoutAsync(UserId, planCode);

        Assert.Equal(PaymentIntentResultStatus.InvalidPlanCode, result.Status);
        Assert.Empty(fixture.Orders.Items);
        Assert.Empty(fixture.Gateway.Requests);
    }

    [Theory]
    [InlineData("TripPass", 49000)]
    [InlineData("Membership", 59000)]
    public async Task Checkout_FreeUser_CreatesPurchaseUsingCatalogAmount(
        string planCode,
        decimal expectedAmount)
    {
        var fixture = new Fixture();

        var result = await fixture.Service.CheckoutAsync(UserId, planCode);

        Assert.Equal(PaymentIntentResultStatus.Success, result.Status);
        var order = Assert.Single(fixture.Orders.Items);
        Assert.Equal(PaymentOrderType.Purchase, order.Type);
        Assert.Equal(expectedAmount, order.Amount);
        Assert.Equal(PaymentOrderStatus.Pending, order.Status);
        Assert.Equal(Now.AddMinutes(15), order.ExpiresAt);
        Assert.Null(order.PaidAt);
        Assert.True(order.ProviderOrderCode > 0);
        var request = Assert.Single(fixture.Gateway.Requests);
        Assert.Equal(order.Id, request.OrderId);
        Assert.Equal(order.ProviderOrderCode, request.ProviderOrderCode);
        Assert.Equal(expectedAmount, request.Amount);
        Assert.Equal(PaymentOrderType.Purchase, request.Type);
    }

    [Theory]
    [InlineData(PlanCode.TripPass)]
    [InlineData(PlanCode.Membership)]
    public async Task Checkout_SameActivePlan_IsRejected(PlanCode activePlan)
    {
        var fixture = new Fixture(subscriptions: [Active(activePlan)]);

        var result = await fixture.Service.CheckoutAsync(UserId, activePlan.ToString());

        Assert.Equal(PaymentIntentResultStatus.PlanAlreadyActive, result.Status);
        Assert.Empty(fixture.Orders.Items);
    }

    [Fact]
    public async Task Checkout_ActiveMembershipCoversTripPass()
    {
        var fixture = new Fixture(subscriptions: [Active(PlanCode.Membership)]);

        var result = await fixture.Service.CheckoutAsync(UserId, "TripPass");

        Assert.Equal(PaymentIntentResultStatus.CoveredByHigherPlan, result.Status);
        Assert.Empty(fixture.Orders.Items);
    }

    [Fact]
    public async Task Checkout_ActiveTripPassAllowsMembershipUpgradeWithoutMutatingSubscription()
    {
        var subscription = Active(PlanCode.TripPass);
        var originalEndsAt = subscription.EndsAt;
        var fixture = new Fixture(subscriptions: [subscription]);

        var result = await fixture.Service.CheckoutAsync(UserId, "Membership");

        Assert.Equal(PaymentIntentResultStatus.Success, result.Status);
        Assert.Equal(PlanCode.Membership, Assert.Single(fixture.Orders.Items).PlanCode);
        Assert.Equal(originalEndsAt, subscription.EndsAt);
    }

    [Fact]
    public async Task Checkout_ExpiredPlanAllowsRepurchase()
    {
        var fixture = new Fixture(subscriptions:
            [new UserSubscription { UserId = UserId, PlanCode = PlanCode.TripPass, EndsAt = Now }]);

        var result = await fixture.Service.CheckoutAsync(UserId, "TripPass");

        Assert.Equal(PaymentIntentResultStatus.Success, result.Status);
    }

    [Fact]
    public async Task Checkout_SecondRequestReusesUsablePendingOrder()
    {
        var fixture = new Fixture();
        var first = await fixture.Service.CheckoutAsync(UserId, "Membership");

        var second = await fixture.Service.CheckoutAsync(UserId, "Membership");

        Assert.Equal(PaymentIntentResultStatus.Success, first.Status);
        Assert.Equal(PaymentIntentResultStatus.PendingOrderExists, second.Status);
        Assert.Equal(first.Response, second.Response);
        Assert.Single(fixture.Orders.Items);
        Assert.Single(fixture.Gateway.Requests);
    }

    [Fact]
    public async Task Checkout_ExpiredPendingIsMarkedExpiredAndReplaced()
    {
        var expired = Pending(PlanCode.TripPass, PaymentOrderType.Purchase, Now);
        var fixture = new Fixture(existingOrders: [expired]);

        var result = await fixture.Service.CheckoutAsync(UserId, "TripPass");

        Assert.Equal(PaymentIntentResultStatus.Success, result.Status);
        Assert.Equal(PaymentOrderStatus.Expired, expired.Status);
        Assert.Equal(2, fixture.Orders.Items.Count);
        Assert.NotEqual(expired.Id, result.Response!.OrderId);
    }

    [Fact]
    public async Task Checkout_IncompletePendingIsFailedAndReplaced()
    {
        var incomplete = Pending(PlanCode.TripPass, PaymentOrderType.Purchase, Now.AddMinutes(5));
        incomplete.CheckoutUrl = null;
        incomplete.QrCode = null;
        var fixture = new Fixture(existingOrders: [incomplete]);

        var result = await fixture.Service.CheckoutAsync(UserId, "TripPass");

        Assert.Equal(PaymentIntentResultStatus.Success, result.Status);
        Assert.Equal(PaymentOrderStatus.Failed, incomplete.Status);
        Assert.Equal(2, fixture.Orders.Items.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Checkout_GatewayFailureOrTimeout_MarksOrderFailed(bool timeout)
    {
        var gateway = timeout
            ? new FakeGateway(_ => throw new TimeoutException())
            : new FakeGateway(_ => PaymentLinkResult.Unavailable());
        var fixture = new Fixture(gateway: gateway);

        var result = await fixture.Service.CheckoutAsync(UserId, "Membership");

        Assert.Equal(PaymentIntentResultStatus.GatewayUnavailable, result.Status);
        Assert.Equal(PaymentOrderStatus.Failed, Assert.Single(fixture.Orders.Items).Status);
    }

    [Fact]
    public async Task Checkout_FailedOrderDoesNotBlockRetry()
    {
        var failed = Pending(PlanCode.Membership, PaymentOrderType.Purchase, Now.AddMinutes(10));
        failed.Status = PaymentOrderStatus.Failed;
        var fixture = new Fixture(existingOrders: [failed]);

        var result = await fixture.Service.CheckoutAsync(UserId, "Membership");

        Assert.Equal(PaymentIntentResultStatus.Success, result.Status);
        Assert.Equal(2, fixture.Orders.Items.Count);
    }

    [Fact]
    public async Task Renew_NoActivePaidPlan_IsRejected()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.RenewAsync(UserId);

        Assert.Equal(PaymentIntentResultStatus.NoActiveSubscription, result.Status);
        Assert.Empty(fixture.Orders.Items);
    }

    [Theory]
    [InlineData(PlanCode.TripPass, 49000)]
    [InlineData(PlanCode.Membership, 59000)]
    public async Task Renew_UsesEffectivePaidPlan(PlanCode activePlan, decimal amount)
    {
        var subscription = Active(activePlan);
        var fixture = new Fixture(subscriptions: [subscription]);

        var result = await fixture.Service.RenewAsync(UserId);

        Assert.Equal(PaymentIntentResultStatus.Success, result.Status);
        var order = Assert.Single(fixture.Orders.Items);
        Assert.Equal(activePlan, order.PlanCode);
        Assert.Equal(PaymentOrderType.Renewal, order.Type);
        Assert.Equal(amount, order.Amount);
        Assert.Equal(Now.AddDays(10), subscription.EndsAt);
    }

    [Fact]
    public async Task Renew_MembershipWinsOverActiveTripPass()
    {
        var fixture = new Fixture(subscriptions:
            [Active(PlanCode.TripPass), Active(PlanCode.Membership)]);

        var result = await fixture.Service.RenewAsync(UserId);

        Assert.Equal(PaymentIntentResultStatus.Success, result.Status);
        Assert.Equal(PlanCode.Membership, Assert.Single(fixture.Orders.Items).PlanCode);
    }

    [Fact]
    public async Task Renew_ReusesPendingRenewalOnly()
    {
        var purchase = Pending(PlanCode.TripPass, PaymentOrderType.Purchase, Now.AddMinutes(10));
        var renewal = Pending(PlanCode.TripPass, PaymentOrderType.Renewal, Now.AddMinutes(10));
        var fixture = new Fixture(
            subscriptions: [Active(PlanCode.TripPass)],
            existingOrders: [purchase, renewal]);

        var result = await fixture.Service.RenewAsync(UserId);

        Assert.Equal(PaymentIntentResultStatus.PendingOrderExists, result.Status);
        Assert.Equal(renewal.Id, result.Response!.OrderId);
        Assert.Empty(fixture.Gateway.Requests);
    }

    [Fact]
    public async Task PersistedAccountIsRequiredBeforePaymentWork()
    {
        var fixture = new Fixture(persistedUser: false);

        var checkout = await fixture.Service.CheckoutAsync(UserId, "TripPass");
        var renew = await fixture.Service.RenewAsync(UserId);

        Assert.Equal(PaymentIntentResultStatus.NonPersistedUser, checkout.Status);
        Assert.Equal(PaymentIntentResultStatus.NonPersistedUser, renew.Status);
        Assert.Empty(fixture.Orders.Items);
    }

    [Fact]
    public async Task GetOrder_ReturnsOnlyOwnedOrder()
    {
        var owned = Pending(PlanCode.TripPass, PaymentOrderType.Purchase, Now.AddMinutes(10));
        var foreign = Pending(PlanCode.Membership, PaymentOrderType.Purchase, Now.AddMinutes(10));
        foreign.UserId = Guid.NewGuid();
        var fixture = new Fixture(existingOrders: [owned, foreign]);

        var ownResult = await fixture.Service.GetOrderAsync(UserId, owned.Id);
        var foreignResult = await fixture.Service.GetOrderAsync(UserId, foreign.Id);
        var missingResult = await fixture.Service.GetOrderAsync(UserId, Guid.NewGuid());

        Assert.Equal(PaymentOrderLookupStatus.Success, ownResult.Status);
        Assert.Equal(owned.Id, ownResult.Response!.OrderId);
        Assert.Equal(PaymentOrderLookupStatus.NotFound, foreignResult.Status);
        Assert.Equal(PaymentOrderLookupStatus.NotFound, missingResult.Status);
    }

    [Fact]
    public async Task GetOrder_NonPersistedUserIsRejectedBeforeLookup()
    {
        var fixture = new Fixture(persistedUser: false);

        var result = await fixture.Service.GetOrderAsync(UserId, Guid.NewGuid());

        Assert.Equal(PaymentOrderLookupStatus.NonPersistedUser, result.Status);
        Assert.Equal(0, fixture.Orders.LookupCalls);
    }

    [Fact]
    public async Task GetOrder_ProviderPaid_UsesSharedSettlementAndReturnsPaid()
    {
        var order = Pending(PlanCode.Membership, PaymentOrderType.Purchase, Now.AddMinutes(5));
        var settlement = new RecordingSettlementService(order);
        var gateway = new FakeGateway(
            _ => PaymentLinkResult.Unavailable(),
            code => new PaymentGatewayOrderResult(
                true,
                code,
                order.Amount,
                PaymentGatewayOrderStatus.Paid));
        var fixture = new Fixture(
            existingOrders: [order],
            gateway: gateway,
            settlementService: settlement);

        var result = await fixture.Service.GetOrderAsync(UserId, order.Id);

        Assert.Equal("Paid", result.Response!.Status);
        Assert.Equal(1, gateway.LookupCalls);
        Assert.Single(settlement.Notifications);
    }

    [Fact]
    public async Task GetOrder_GatewayUnavailable_PreservesRetryableLocalState()
    {
        var order = Pending(PlanCode.TripPass, PaymentOrderType.Purchase, Now.AddMinutes(5));
        var gateway = new FakeGateway(
            _ => PaymentLinkResult.Unavailable(),
            PaymentGatewayOrderResult.Unavailable);
        var fixture = new Fixture(existingOrders: [order], gateway: gateway);

        var result = await fixture.Service.GetOrderAsync(UserId, order.Id);

        Assert.Equal(PaymentOrderLookupStatus.Success, result.Status);
        Assert.Equal("Pending", result.Response!.Status);
        Assert.Equal(PaymentOrderStatus.Pending, order.Status);
    }

    [Fact]
    public async Task GetOrder_ProviderCancelled_UsesSettlementFailurePath()
    {
        var order = Pending(PlanCode.TripPass, PaymentOrderType.Purchase, Now.AddMinutes(5));
        var settlement = new RecordingSettlementService(order);
        var gateway = new FakeGateway(
            _ => PaymentLinkResult.Unavailable(),
            code => new PaymentGatewayOrderResult(
                true,
                code,
                order.Amount,
                PaymentGatewayOrderStatus.Cancelled));
        var fixture = new Fixture(
            existingOrders: [order],
            gateway: gateway,
            settlementService: settlement);

        var result = await fixture.Service.GetOrderAsync(UserId, order.Id);

        Assert.Equal("Failed", result.Response!.Status);
        Assert.False(Assert.Single(settlement.Notifications).IsSuccessful);
    }

    [Fact]
    public async Task GetOrder_ProviderPendingPastLocalExpiry_MarksExpired()
    {
        var order = Pending(PlanCode.TripPass, PaymentOrderType.Purchase, Now.AddSeconds(-1));
        var gateway = new FakeGateway(
            _ => PaymentLinkResult.Unavailable(),
            code => new PaymentGatewayOrderResult(
                true,
                code,
                order.Amount,
                PaymentGatewayOrderStatus.Pending));
        var fixture = new Fixture(existingOrders: [order], gateway: gateway);

        var result = await fixture.Service.GetOrderAsync(UserId, order.Id);

        Assert.Equal("Expired", result.Response!.Status);
    }

    [Fact]
    public async Task GetOrder_PaidOrderDoesNotQueryProvider()
    {
        var order = Pending(PlanCode.TripPass, PaymentOrderType.Purchase, Now.AddMinutes(5));
        order.Status = PaymentOrderStatus.Paid;
        var gateway = new FakeGateway(_ => PaymentLinkResult.Unavailable());
        var fixture = new Fixture(existingOrders: [order], gateway: gateway);

        var result = await fixture.Service.GetOrderAsync(UserId, order.Id);

        Assert.Equal("Paid", result.Response!.Status);
        Assert.Equal(0, gateway.LookupCalls);
    }

    private static UserSubscription Active(PlanCode planCode) =>
        new() { UserId = UserId, PlanCode = planCode, EndsAt = Now.AddDays(10) };

    private static PaymentOrder Pending(
        PlanCode planCode,
        PaymentOrderType type,
        DateTime expiresAt) =>
        new()
        {
            UserId = UserId,
            PlanCode = planCode,
            Type = type,
            Amount = SubscriptionCatalog.Get(planCode).Price,
            Status = PaymentOrderStatus.Pending,
            ProviderOrderCode = 100,
            CheckoutUrl = "https://checkout.test/existing",
            QrCode = "existing-qr",
            ExpiresAt = expiresAt,
            CreatedAt = Now.AddMinutes(-1)
        };

    private sealed class Fixture
    {
        public Fixture(
            bool persistedUser = true,
            IReadOnlyList<UserSubscription>? subscriptions = null,
            IReadOnlyList<PaymentOrder>? existingOrders = null,
            FakeGateway? gateway = null,
            IPaymentSettlementService? settlementService = null)
        {
            Orders = new FakePaymentOrderRepository(existingOrders ?? []);
            Gateway = gateway ?? new FakeGateway(_ =>
                PaymentLinkResult.Succeeded("https://checkout.test/new", "new-qr"));
            Service = new PaymentService(
                new FakeUserRepository(persistedUser),
                new FakeSubscriptionRepository(subscriptions ?? []),
                Orders,
                new FakePaymentOperationExecutor(persistedUser),
                Gateway,
                settlementService ?? new FakeSettlementService(),
                new FixedTimeProvider(Now),
                NullLogger<PaymentService>.Instance);
        }

        public PaymentService Service { get; }
        public FakePaymentOrderRepository Orders { get; }
        public FakeGateway Gateway { get; }
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class FakePaymentOperationExecutor(bool persistedUser) : IPaymentOperationExecutor
    {
        public async Task<PaymentOperationExecution<T>> ExecuteForUserAsync<T>(
            Guid userId,
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default) =>
            persistedUser
                ? new PaymentOperationExecution<T>(true, await operation(cancellationToken))
                : new PaymentOperationExecution<T>(false, default);
    }

    private sealed class FakePaymentOrderRepository(IReadOnlyList<PaymentOrder> existing)
        : IPaymentOrderRepository
    {
        private long nextProviderCode = 1000;
        public List<PaymentOrder> Items { get; } = [.. existing];
        public int LookupCalls { get; private set; }

        public Task<PaymentOrder?> GetPendingAsync(
            Guid userId, PlanCode planCode, PaymentOrderType type,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items
                .Where(order => order.UserId == userId && order.PlanCode == planCode
                                && order.Type == type && order.Status == PaymentOrderStatus.Pending)
                .OrderByDescending(order => order.CreatedAt)
                .FirstOrDefault());

        public Task<PaymentOrder?> GetOwnedByIdAsync(
            Guid orderId, Guid userId, CancellationToken cancellationToken = default)
        {
            LookupCalls++;
            return Task.FromResult(Items.SingleOrDefault(order =>
                order.Id == orderId && order.UserId == userId));
        }

        public Task AddAsync(PaymentOrder order, CancellationToken cancellationToken = default)
        {
            order.ProviderOrderCode = ++nextProviderCode;
            order.CreatedAt = Now;
            Items.Add(order);
            return Task.CompletedTask;
        }

        public Task<bool> MarkExpiredIfPendingAsync(
            Guid orderId,
            DateTime updatedAt,
            CancellationToken cancellationToken = default)
        {
            var order = Items.Single(candidate => candidate.Id == orderId);
            if (order.Status != PaymentOrderStatus.Pending)
            {
                return Task.FromResult(false);
            }

            order.Status = PaymentOrderStatus.Expired;
            order.UpdatedAt = updatedAt;
            return Task.FromResult(true);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class FakeGateway(
        Func<PaymentLinkRequest, PaymentLinkResult> create,
        Func<long, PaymentGatewayOrderResult>? lookup = null)
        : IPaymentGateway
    {
        public List<PaymentLinkRequest> Requests { get; } = [];
        public int LookupCalls { get; private set; }

        public Task<PaymentLinkResult> CreatePaymentLinkAsync(
            PaymentLinkRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return Task.FromResult(create(request));
        }

        public Task<PaymentGatewayOrderResult> GetPaymentAsync(
            long providerOrderCode,
            CancellationToken cancellationToken = default)
        {
            LookupCalls++;
            return Task.FromResult(lookup?.Invoke(providerOrderCode)
                                   ?? PaymentGatewayOrderResult.Unavailable(providerOrderCode));
        }

        public Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(
            string rawPayload,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PaymentWebhookVerificationResult.Invalid());
    }

    private sealed class FakeSubscriptionRepository(IReadOnlyList<UserSubscription> subscriptions)
        : ISubscriptionRepository
    {
        public Task<IReadOnlyList<UserSubscription>> GetByUserIdAsync(
            Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserSubscription>>(
                subscriptions.Where(subscription => subscription.UserId == userId).ToArray());

        public Task<UserSubscription?> GetByUserAndPlanAsync(
            Guid userId, PlanCode planCode, CancellationToken cancellationToken = default) =>
            Task.FromResult(subscriptions.SingleOrDefault(subscription =>
                subscription.UserId == userId && subscription.PlanCode == planCode));

        public Task AddAsync(
            UserSubscription subscription,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeSettlementService : IPaymentSettlementService
    {
        public Task<PaymentSettlementResult> ApplyVerifiedPaymentAsync(
            VerifiedPaymentNotification notification,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new PaymentSettlementResult(PaymentSettlementStatus.UnknownOrder));
    }

    private sealed class RecordingSettlementService(PaymentOrder order)
        : IPaymentSettlementService
    {
        public List<VerifiedPaymentNotification> Notifications { get; } = [];

        public Task<PaymentSettlementResult> ApplyVerifiedPaymentAsync(
            VerifiedPaymentNotification notification,
            CancellationToken cancellationToken = default)
        {
            Notifications.Add(notification);
            if (notification.IsSuccessful && notification.Amount == order.Amount)
            {
                order.Status = PaymentOrderStatus.Paid;
                order.PaidAt = Now;
                return Task.FromResult(
                    new PaymentSettlementResult(PaymentSettlementStatus.Settled));
            }

            order.Status = PaymentOrderStatus.Failed;
            return Task.FromResult(
                new PaymentSettlementResult(PaymentSettlementStatus.NonSuccessful));
        }
    }

    private sealed class FakeUserRepository(bool persistedUser) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(persistedUser ? new User { Id = userId } : null);

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<User?> GetByIdForUpdateAsync(Guid userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UpdatePasswordHashAsync(User user, string passwordHash, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SaveProfileChangesAsync(User user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
