using LocalMateAI.Application.DTOs.Email;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

public sealed class EmailOutboxRepositoryPostgresTests
{

    [Fact]
    public async Task Enqueue_SameDeduplicationKeyTwice_InsertsOnce()
    {
        var connectionString = PostgresTestDatabase.RequireConnection();

        var key = NewKey();
        try
        {
            await using var context = CreateContext(connectionString);
            var repository = new EmailOutboxRepository(context);
            var now = DateTime.UtcNow;

            var first = await repository.EnqueueAsync(NewEntry(key), now);
            var second = await repository.EnqueueAsync(NewEntry(key), now);

            Assert.True(first);
            Assert.False(second);
            Assert.Equal(1, await context.EmailOutboxMessages.CountAsync(message => message.DeduplicationKey == key));
        }
        finally
        {
            await CleanupAsync(connectionString, [key]);
        }
    }

    [Fact]
    public async Task Enqueue_DuplicateInsideTransaction_DoesNotAbortTransaction()
    {
        var connectionString = PostgresTestDatabase.RequireConnection();

        var key = NewKey();
        var otherKey = NewKey();
        try
        {
            await using var context = CreateContext(connectionString);
            var repository = new EmailOutboxRepository(context);
            var now = DateTime.UtcNow;
            await repository.EnqueueAsync(NewEntry(key), now);

            await using (var transaction = await context.Database.BeginTransactionAsync())
            {
                Assert.False(await repository.EnqueueAsync(NewEntry(key), now));
                // Transaction vẫn dùng tiếp được sau lần trùng.
                Assert.True(await repository.EnqueueAsync(NewEntry(otherKey), now));
                await transaction.CommitAsync();
            }

            Assert.Equal(1, await context.EmailOutboxMessages.CountAsync(message => message.DeduplicationKey == otherKey));
        }
        finally
        {
            await CleanupAsync(connectionString, [key, otherKey]);
        }
    }

    [Fact]
    public async Task ClaimDue_ConcurrentClaims_NeverReturnSameMessage()
    {
        var connectionString = PostgresTestDatabase.RequireConnection();

        var keys = Enumerable.Range(0, 10).Select(_ => NewKey()).ToArray();
        try
        {
            var now = DateTime.UtcNow;
            await using (var seed = CreateContext(connectionString))
            {
                var seedRepository = new EmailOutboxRepository(seed);
                foreach (var key in keys)
                {
                    await seedRepository.EnqueueAsync(NewEntry(key), now.AddMinutes(-1));
                }
            }

            var contexts = Enumerable.Range(0, 4).Select(_ => CreateContext(connectionString)).ToArray();
            try
            {
                var claims = await Task.WhenAll(contexts.Select(context =>
                    new EmailOutboxRepository(context).ClaimDueAsync(now, now.AddMinutes(5), 20)));

                var claimedIds = claims.SelectMany(batch => batch)
                    .Where(message => message.ToEmail == TestEmail)
                    .Select(message => message.Id)
                    .ToList();

                Assert.Equal(keys.Length, claimedIds.Count);
                Assert.Equal(claimedIds.Count, claimedIds.Distinct().Count());
                Assert.All(claims.SelectMany(batch => batch), message => Assert.Equal(1, message.AttemptCount));
            }
            finally
            {
                foreach (var context in contexts)
                {
                    await context.DisposeAsync();
                }
            }
        }
        finally
        {
            await CleanupAsync(connectionString, keys);
        }
    }

    [Fact]
    public async Task ClaimDue_LeasedMessage_IsNotClaimedAgainUntilLeaseExpires()
    {
        var connectionString = PostgresTestDatabase.RequireConnection();

        var key = NewKey();
        try
        {
            await using var context = CreateContext(connectionString);
            var repository = new EmailOutboxRepository(context);
            var now = DateTime.UtcNow;
            await repository.EnqueueAsync(NewEntry(key), now.AddMinutes(-1));

            var first = await repository.ClaimDueAsync(now, now.AddMinutes(5), 20);
            var duringLease = await repository.ClaimDueAsync(now.AddMinutes(1), now.AddMinutes(6), 20);
            var afterLease = await repository.ClaimDueAsync(now.AddMinutes(6), now.AddMinutes(11), 20);

            Assert.Contains(first, message => message.ToEmail == TestEmail);
            Assert.DoesNotContain(duringLease, message => message.ToEmail == TestEmail);
            var reclaimed = Assert.Single(afterLease, message => message.ToEmail == TestEmail);
            Assert.Equal(2, reclaimed.AttemptCount);
        }
        finally
        {
            await CleanupAsync(connectionString, [key]);
        }
    }

    [Fact]
    public async Task MarkSent_ThenDeleteExpired_RemovesOnlyOldSentMessages()
    {
        var connectionString = PostgresTestDatabase.RequireConnection();

        var oldKey = NewKey();
        var recentKey = NewKey();
        try
        {
            await using var context = CreateContext(connectionString);
            var repository = new EmailOutboxRepository(context);
            var now = DateTime.UtcNow;
            await repository.EnqueueAsync(NewEntry(oldKey), now);
            await repository.EnqueueAsync(NewEntry(recentKey), now);
            var ids = await context.EmailOutboxMessages
                .Where(message => message.DeduplicationKey == oldKey || message.DeduplicationKey == recentKey)
                .ToDictionaryAsync(message => message.DeduplicationKey, message => message.Id);

            await repository.MarkSentAsync(ids[oldKey], now.AddDays(-8));
            await repository.MarkSentAsync(ids[recentKey], now.AddDays(-1));

            await repository.DeleteExpiredAsync(now.AddDays(-7), now.AddDays(-30));

            var remaining = await context.EmailOutboxMessages
                .AsNoTracking()
                .Where(message => message.DeduplicationKey == oldKey || message.DeduplicationKey == recentKey)
                .ToListAsync();
            var message = Assert.Single(remaining);
            Assert.Equal(recentKey, message.DeduplicationKey);
            Assert.Equal(EmailOutboxStatus.Sent, message.Status);
        }
        finally
        {
            await CleanupAsync(connectionString, [oldKey, recentKey]);
        }
    }

    private const string TestEmail = "outbox-test@localmate.test";

    private static string NewKey() => $"test-outbox:{Guid.NewGuid()}";

    private static EmailOutboxEntry NewEntry(string key) =>
        new(TestEmail, "Test", "payment-receipt", """{"fullName":"Test"}""", key);

    private static AppDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;
        return new AppDbContext(options);
    }

    private static async Task CleanupAsync(string connectionString, IReadOnlyCollection<string> keys)
    {
        await using var context = CreateContext(connectionString);
        await context.EmailOutboxMessages
            .Where(message => keys.Contains(message.DeduplicationKey))
            .ExecuteDeleteAsync();
    }
}
