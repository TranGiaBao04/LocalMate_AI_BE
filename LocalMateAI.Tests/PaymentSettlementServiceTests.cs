using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMateAI.Tests;

public sealed class PaymentSettlementServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly DateTime Now = new(2026, 10, 5, 4, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(PlanCode.TripPass, 7, 49000)]
    [InlineData(PlanCode.Membership, 30, 59000)]
    public async Task CorrectPayment_CreatesSubscriptionAndMarksOrderPaid(
        PlanCode planCode,
        int durationDays,
        decimal amount)
    {
        var fixture = new Fixture(Order(planCode, amount));

        var result = await fixture.Service.ApplyVerifiedPaymentAsync(
            new VerifiedPaymentNotification(fixture.Order.ProviderOrderCode, amount, true));

        Assert.Equal(PaymentSettlementStatus.Settled, result.Status);
        Assert.Equal(PaymentOrderStatus.Paid, fixture.Order.Status);
        Assert.Equal(Now, fixture.Order.PaidAt);
        var subscription = Assert.Single(fixture.Subscriptions.Items);
        Assert.Equal(Now, subscription.StartsAt);
        Assert.Equal(Now.AddDays(durationDays), subscription.EndsAt);
    }

    [Fact]
    public async Task ActiveSubscription_ExtendsFromExistingEndAndPreservesStart()
    {
        var startsAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var subscription = new UserSubscription
        {
            UserId = UserId,
            PlanCode = PlanCode.Membership,
            StartsAt = startsAt,
            EndsAt = new DateTime(2026, 10, 20, 0, 0, 0, DateTimeKind.Utc)
        };
        var fixture = new Fixture(Order(PlanCode.Membership, 59000), [subscription]);

        await fixture.Service.ApplyVerifiedPaymentAsync(
            new VerifiedPaymentNotification(fixture.Order.ProviderOrderCode, 59000, true));

        Assert.Equal(startsAt, subscription.StartsAt);
        Assert.Equal(new DateTime(2026, 11, 19, 0, 0, 0, DateTimeKind.Utc), subscription.EndsAt);
    }

    [Fact]
    public async Task ExpiredSubscription_RestartsFromSettlementTime()
    {
        var subscription = new UserSubscription
        {
            UserId = UserId,
            PlanCode = PlanCode.Membership,
            StartsAt = Now.AddDays(-60),
            EndsAt = Now.AddSeconds(-1)
        };
        var fixture = new Fixture(Order(PlanCode.Membership, 59000), [subscription]);

        await fixture.Service.ApplyVerifiedPaymentAsync(
            new VerifiedPaymentNotification(fixture.Order.ProviderOrderCode, 59000, true));

        Assert.Equal(Now, subscription.StartsAt);
        Assert.Equal(Now.AddDays(30), subscription.EndsAt);
    }

    [Fact]
    public async Task AmountMismatch_FailsOrderWithoutEntitlement()
    {
        var fixture = new Fixture(Order(PlanCode.TripPass, 49000));

        var result = await fixture.Service.ApplyVerifiedPaymentAsync(
            new VerifiedPaymentNotification(fixture.Order.ProviderOrderCode, 48000, true));

        Assert.Equal(PaymentSettlementStatus.AmountMismatch, result.Status);
        Assert.Equal(PaymentOrderStatus.Failed, fixture.Order.Status);
        Assert.Null(fixture.Order.PaidAt);
        Assert.Empty(fixture.Subscriptions.Items);
    }

    [Fact]
    public async Task VerifiedNonSuccess_FailsOrderWithoutEntitlement()
    {
        var fixture = new Fixture(Order(PlanCode.TripPass, 49000));

        var result = await fixture.Service.ApplyVerifiedPaymentAsync(
            new VerifiedPaymentNotification(fixture.Order.ProviderOrderCode, 49000, false));

        Assert.Equal(PaymentSettlementStatus.NonSuccessful, result.Status);
        Assert.Equal(PaymentOrderStatus.Failed, fixture.Order.Status);
        Assert.Empty(fixture.Subscriptions.Items);
    }

    [Theory]
    [InlineData(PaymentOrderStatus.Expired)]
    [InlineData(PaymentOrderStatus.Failed)]
    public async Task LateCorrectPayment_SettlesNonPaidOrder(PaymentOrderStatus initialStatus)
    {
        var order = Order(PlanCode.TripPass, 49000);
        order.Status = initialStatus;
        var fixture = new Fixture(order);

        var result = await fixture.Service.ApplyVerifiedPaymentAsync(
            new VerifiedPaymentNotification(order.ProviderOrderCode, 49000, true));

        Assert.Equal(PaymentSettlementStatus.Settled, result.Status);
        Assert.Equal(PaymentOrderStatus.Paid, order.Status);
        Assert.Equal(Now.AddDays(7), Assert.Single(fixture.Subscriptions.Items).EndsAt);
    }

    [Theory]
    [InlineData(PaymentOrderType.Purchase)]
    [InlineData(PaymentOrderType.Renewal)]
    public async Task OrderType_DoesNotChangeSettlementFormula(PaymentOrderType type)
    {
        var order = Order(PlanCode.TripPass, 49000);
        order.Type = type;
        var fixture = new Fixture(order);

        await fixture.Service.ApplyVerifiedPaymentAsync(
            new VerifiedPaymentNotification(order.ProviderOrderCode, 49000, true));

        Assert.Equal(Now.AddDays(7), Assert.Single(fixture.Subscriptions.Items).EndsAt);
    }

    [Fact]
    public async Task PaidOrder_IsTerminalAndDoesNotExtendTwiceOrChangePaidAt()
    {
        var fixture = new Fixture(Order(PlanCode.Membership, 59000));
        var notification = new VerifiedPaymentNotification(
            fixture.Order.ProviderOrderCode,
            59000,
            true);
        var first = await fixture.Service.ApplyVerifiedPaymentAsync(notification);
        var paidAt = fixture.Order.PaidAt;
        var second = await fixture.Service.ApplyVerifiedPaymentAsync(notification);
        var laterFailure = await fixture.Service.ApplyVerifiedPaymentAsync(notification with
        {
            IsSuccessful = false
        });

        Assert.Equal(PaymentSettlementStatus.Settled, first.Status);
        Assert.Equal(PaymentSettlementStatus.AlreadyPaid, second.Status);
        Assert.Equal(PaymentSettlementStatus.AlreadyPaid, laterFailure.Status);
        Assert.Equal(paidAt, fixture.Order.PaidAt);
        Assert.Equal(Now.AddDays(30), Assert.Single(fixture.Subscriptions.Items).EndsAt);
    }

    [Fact]
    public async Task UnknownOrder_ReturnsSafeNoOp()
    {
        var fixture = new Fixture(Order(PlanCode.TripPass, 49000));

        var result = await fixture.Service.ApplyVerifiedPaymentAsync(
            new VerifiedPaymentNotification(999999, 49000, true));

        Assert.Equal(PaymentSettlementStatus.UnknownOrder, result.Status);
        Assert.Empty(fixture.Subscriptions.Items);
    }

    private static PaymentOrder Order(PlanCode planCode, decimal amount) => new()
    {
        UserId = UserId,
        PlanCode = planCode,
        Type = PaymentOrderType.Purchase,
        Amount = amount,
        Status = PaymentOrderStatus.Pending,
        ProviderOrderCode = 12001,
        ExpiresAt = Now.AddMinutes(15)
    };

    private sealed class Fixture
    {
        public Fixture(
            PaymentOrder order,
            IReadOnlyList<UserSubscription>? subscriptions = null)
        {
            Order = order;
            Subscriptions = new FakeSubscriptionRepository(subscriptions ?? []);
            Service = new PaymentSettlementService(
                new FakeSettlementExecutor([order]),
                Subscriptions,
                new FixedTimeProvider(Now),
                NullLogger<PaymentSettlementService>.Instance);
        }

        public PaymentOrder Order { get; }
        public FakeSubscriptionRepository Subscriptions { get; }
        public PaymentSettlementService Service { get; }
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }

    private sealed class FakeSettlementExecutor(IReadOnlyList<PaymentOrder> orders)
        : IPaymentSettlementExecutor
    {
        public async Task<PaymentSettlementExecution<T>> ExecuteAsync<T>(
            long providerOrderCode,
            Func<PaymentOrder, CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            var order = orders.SingleOrDefault(candidate =>
                candidate.ProviderOrderCode == providerOrderCode);
            return order is null
                ? new PaymentSettlementExecution<T>(false, default)
                : new PaymentSettlementExecution<T>(
                    true,
                    await operation(order, cancellationToken));
        }
    }

    private sealed class FakeSubscriptionRepository(IReadOnlyList<UserSubscription> subscriptions)
        : ISubscriptionRepository
    {
        public List<UserSubscription> Items { get; } = [.. subscriptions];

        public Task<IReadOnlyList<UserSubscription>> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<UserSubscription>>(
                Items.Where(subscription => subscription.UserId == userId).ToArray());

        public Task<UserSubscription?> GetByUserAndPlanAsync(
            Guid userId,
            PlanCode planCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Items.SingleOrDefault(subscription =>
                subscription.UserId == userId && subscription.PlanCode == planCode));

        public Task AddAsync(
            UserSubscription subscription,
            CancellationToken cancellationToken = default)
        {
            Items.Add(subscription);
            return Task.CompletedTask;
        }
    }

}
