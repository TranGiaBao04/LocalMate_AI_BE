using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMateAI.Tests;

public sealed class PaymentSettlementConcurrencyPostgresTests
{
    private const string ConnectionEnvironmentVariable = "LOCALMATE_TEST_CONNECTION";
    private static readonly DateTime Now =
        new(2026, 10, 5, 4, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ConcurrentDuplicateSettlement_ExtendsExactlyOnce()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var userId = Guid.NewGuid();
        var order = await SeedAsync(connectionString, userId, orderCount: 1);
        await using var first = CreateScope(connectionString);
        await using var second = CreateScope(connectionString);

        try
        {
            var notification = new VerifiedPaymentNotification(
                order[0].ProviderOrderCode,
                order[0].Amount,
                true);
            var results = await Task.WhenAll(
                first.Service.ApplyVerifiedPaymentAsync(notification),
                second.Service.ApplyVerifiedPaymentAsync(notification));

            Assert.Contains(results, result => result.Status == PaymentSettlementStatus.Settled);
            Assert.Contains(results, result => result.Status == PaymentSettlementStatus.AlreadyPaid);

            await using var verify = CreateContext(connectionString);
            var storedOrder = await verify.PaymentOrders.AsNoTracking()
                .SingleAsync(candidate => candidate.Id == order[0].Id);
            var subscription = await verify.UserSubscriptions.AsNoTracking()
                .SingleAsync(candidate => candidate.UserId == userId
                                          && candidate.PlanCode == PlanCode.Membership);
            Assert.Equal(PaymentOrderStatus.Paid, storedOrder.Status);
            Assert.Equal(Now, storedOrder.PaidAt);
            Assert.Equal(Now.AddDays(30), subscription.EndsAt);
        }
        finally
        {
            await CleanupAsync(connectionString, userId);
        }
    }

    [Fact]
    public async Task ConcurrentDistinctOrders_ExtendSamePlanTwiceWithoutLostUpdate()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var userId = Guid.NewGuid();
        var orders = await SeedAsync(connectionString, userId, orderCount: 2);
        await using var first = CreateScope(connectionString);
        await using var second = CreateScope(connectionString);

        try
        {
            var results = await Task.WhenAll(
                first.Service.ApplyVerifiedPaymentAsync(
                    new VerifiedPaymentNotification(
                        orders[0].ProviderOrderCode,
                        orders[0].Amount,
                        true)),
                second.Service.ApplyVerifiedPaymentAsync(
                    new VerifiedPaymentNotification(
                        orders[1].ProviderOrderCode,
                        orders[1].Amount,
                        true)));

            Assert.All(
                results,
                result => Assert.Equal(PaymentSettlementStatus.Settled, result.Status));

            await using var verify = CreateContext(connectionString);
            var statuses = await verify.PaymentOrders.AsNoTracking()
                .Where(candidate => candidate.UserId == userId)
                .Select(candidate => candidate.Status)
                .ToListAsync();
            var subscription = await verify.UserSubscriptions.AsNoTracking()
                .SingleAsync(candidate => candidate.UserId == userId
                                          && candidate.PlanCode == PlanCode.Membership);
            Assert.Equal(2, statuses.Count);
            Assert.All(statuses, status => Assert.Equal(PaymentOrderStatus.Paid, status));
            Assert.Equal(Now.AddDays(60), subscription.EndsAt);
        }
        finally
        {
            await CleanupAsync(connectionString, userId);
        }
    }

    private static async Task<PaymentOrder[]> SeedAsync(
        string connectionString,
        Guid userId,
        int orderCount)
    {
        await using var context = CreateContext(connectionString);
        context.Users.Add(new User
        {
            Id = userId,
            FullName = "Settlement concurrency test",
            Email = $"settlement-concurrency-{userId:N}@localmate.test",
            Role = UserRole.User
        });
        var orders = Enumerable.Range(0, orderCount).Select(_ => new PaymentOrder
        {
            UserId = userId,
            PlanCode = PlanCode.Membership,
            Type = PaymentOrderType.Purchase,
            Amount = 59000,
            Status = PaymentOrderStatus.Pending,
            ExpiresAt = Now.AddMinutes(15)
        }).ToArray();
        context.PaymentOrders.AddRange(orders);
        await context.SaveChangesAsync();
        return orders;
    }

    private static async Task CleanupAsync(string connectionString, Guid userId)
    {
        await using var context = CreateContext(connectionString);
        await context.UserSubscriptions
            .Where(subscription => subscription.UserId == userId)
            .ExecuteDeleteAsync();
        await context.PaymentOrders
            .Where(order => order.UserId == userId)
            .ExecuteDeleteAsync();
        await context.Users
            .Where(user => user.Id == userId)
            .ExecuteDeleteAsync();
    }

    private static SettlementScope CreateScope(string connectionString)
    {
        var context = CreateContext(connectionString);
        var service = new PaymentSettlementService(
            new PaymentSettlementExecutor(context),
            new SubscriptionRepository(context),
            new FixedTimeProvider(Now),
            NullLogger<PaymentSettlementService>.Instance);
        return new SettlementScope(context, service);
    }

    private static AppDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;
        return new AppDbContext(options);
    }

    private sealed record SettlementScope(
        AppDbContext Context,
        PaymentSettlementService Service) : IAsyncDisposable
    {
        public ValueTask DisposeAsync() => Context.DisposeAsync();
    }

    private sealed class FixedTimeProvider(DateTime now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(now);
    }
}
