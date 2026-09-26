using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMateAI.Tests;

public sealed class PaymentConcurrencyPostgresTests
{
    private const string ConnectionEnvironmentVariable = "LOCALMATE_TEST_CONNECTION";

    [Fact]
    public async Task SameUserConcurrentCheckout_CreatesOneUsablePendingOrder()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var userId = Guid.NewGuid();
        await SeedUsersAsync(connectionString, [userId]);
        var gateway = new BlockingSuccessGateway();

        try
        {
            await using var firstScope = CreateScope(connectionString, gateway);
            await using var secondScope = CreateScope(connectionString, gateway);

            var firstTask = firstScope.Service.CheckoutAsync(userId, "Membership");
            await gateway.FirstCallEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            var secondTask = secondScope.Service.CheckoutAsync(userId, "Membership");
            await Task.Delay(100);
            gateway.ReleaseFirstCall.TrySetResult();

            var results = await Task.WhenAll(firstTask, secondTask);

            Assert.Contains(results, result => result.Status == PaymentIntentResultStatus.Success);
            Assert.Contains(results, result => result.Status == PaymentIntentResultStatus.PendingOrderExists);
            Assert.Equal(1, gateway.CallCount);

            await using var verify = CreateContext(connectionString);
            var order = Assert.Single(await verify.PaymentOrders
                .AsNoTracking()
                .Where(candidate => candidate.UserId == userId)
                .ToListAsync());
            Assert.Equal(PaymentOrderStatus.Pending, order.Status);
            Assert.True(order.ProviderOrderCode > 0);
            Assert.False(string.IsNullOrWhiteSpace(order.CheckoutUrl));
            Assert.False(string.IsNullOrWhiteSpace(order.QrCode));
        }
        finally
        {
            gateway.ReleaseFirstCall.TrySetResult();
            await CleanupAsync(connectionString, [userId]);
        }
    }

    [Fact]
    public async Task ConcurrentCheckoutsAcrossUsers_GeneratePositiveUniqueProviderCodes()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var userIds = Enumerable.Range(0, 8).Select(_ => Guid.NewGuid()).ToArray();
        await SeedUsersAsync(connectionString, userIds);
        var gateway = new ImmediateSuccessGateway();
        var scopes = userIds.Select(_ => CreateScope(connectionString, gateway)).ToArray();

        try
        {
            var results = await Task.WhenAll(userIds.Select((userId, index) =>
                scopes[index].Service.CheckoutAsync(userId, "TripPass")));

            Assert.All(results, result => Assert.Equal(PaymentIntentResultStatus.Success, result.Status));
            await using var verify = CreateContext(connectionString);
            var codes = await verify.PaymentOrders
                .AsNoTracking()
                .Where(order => userIds.Contains(order.UserId))
                .Select(order => order.ProviderOrderCode)
                .ToListAsync();
            Assert.Equal(userIds.Length, codes.Count);
            Assert.All(codes, code => Assert.True(code > 0));
            Assert.Equal(codes.Count, codes.Distinct().Count());
        }
        finally
        {
            foreach (var scope in scopes)
            {
                await scope.DisposeAsync();
            }

            await CleanupAsync(connectionString, userIds);
        }
    }

    private static async Task SeedUsersAsync(string connectionString, IReadOnlyList<Guid> userIds)
    {
        await using var context = CreateContext(connectionString);
        context.Users.AddRange(userIds.Select(userId => new User
        {
            Id = userId,
            FullName = "Payment concurrency test",
            Email = $"payment-concurrency-{userId:N}@localmate.test",
            Role = UserRole.User
        }));
        await context.SaveChangesAsync();
    }

    private static async Task CleanupAsync(string connectionString, IReadOnlyList<Guid> userIds)
    {
        await using var context = CreateContext(connectionString);
        await context.PaymentOrders.Where(order => userIds.Contains(order.UserId)).ExecuteDeleteAsync();
        await context.Users.Where(user => userIds.Contains(user.Id)).ExecuteDeleteAsync();
    }

    private static PaymentServiceScope CreateScope(
        string connectionString,
        IPaymentGateway gateway)
    {
        var context = CreateContext(connectionString);
        var subscriptionRepository = new SubscriptionRepository(context);
        var settlementService = new PaymentSettlementService(
            new PaymentSettlementExecutor(context),
            subscriptionRepository,
            TimeProvider.System,
            NullLogger<PaymentSettlementService>.Instance);
        var service = new PaymentService(
            new UserRepository(context),
            subscriptionRepository,
            new PaymentOrderRepository(context),
            new PaymentOperationExecutor(context),
            gateway,
            settlementService,
            TimeProvider.System,
            NullLogger<PaymentService>.Instance);
        return new PaymentServiceScope(context, service);
    }

    private static AppDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;
        return new AppDbContext(options);
    }

    private sealed record PaymentServiceScope(AppDbContext Context, PaymentService Service)
        : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class BlockingSuccessGateway : IPaymentGateway
    {
        private int callCount;

        public TaskCompletionSource FirstCallEntered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ReleaseFirstCall { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount => callCount;

        public async Task<PaymentLinkResult> CreatePaymentLinkAsync(
            PaymentLinkRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref callCount);
            FirstCallEntered.TrySetResult();
            await ReleaseFirstCall.Task.WaitAsync(cancellationToken);
            return PaymentLinkResult.Succeeded(
                $"https://checkout.test/{request.ProviderOrderCode}",
                $"qr-{request.ProviderOrderCode}");
        }

        public Task<PaymentGatewayOrderResult> GetPaymentAsync(
            long providerOrderCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PaymentGatewayOrderResult.Unavailable(providerOrderCode));

        public Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(
            string rawPayload,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PaymentWebhookVerificationResult.Invalid());
    }

    private sealed class ImmediateSuccessGateway : IPaymentGateway
    {
        public Task<PaymentLinkResult> CreatePaymentLinkAsync(
            PaymentLinkRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PaymentLinkResult.Succeeded(
                $"https://checkout.test/{request.ProviderOrderCode}",
                $"qr-{request.ProviderOrderCode}"));

        public Task<PaymentGatewayOrderResult> GetPaymentAsync(
            long providerOrderCode,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PaymentGatewayOrderResult.Unavailable(providerOrderCode));

        public Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(
            string rawPayload,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PaymentWebhookVerificationResult.Invalid());
    }
}
