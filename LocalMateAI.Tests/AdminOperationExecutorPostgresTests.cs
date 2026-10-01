using System.Diagnostics;
using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

public sealed class AdminOperationExecutorPostgresTests
{

    [Fact]
    public async Task ExecuteExclusiveAsync_SameLockName_RunsOperationsOneAfterAnother()
    {
        var connectionString = PostgresTestDatabase.RequireConnection();

        var lockName = $"test-lock:{Guid.NewGuid():N}";
        var clock = Stopwatch.StartNew();
        TimeSpan firstEnded = default;
        TimeSpan secondStarted = default;
        var firstHoldsLock = new TaskCompletionSource();

        await using var firstContext = CreateContext(connectionString);
        await using var secondContext = CreateContext(connectionString);

        var first = new AdminOperationExecutor(firstContext).ExecuteExclusiveAsync(lockName, async _ =>
        {
            firstHoldsLock.SetResult();
            await Task.Delay(TimeSpan.FromMilliseconds(600));
            firstEnded = clock.Elapsed;
            return true;
        });

        await firstHoldsLock.Task;
        var second = new AdminOperationExecutor(secondContext).ExecuteExclusiveAsync(lockName, _ =>
        {
            secondStarted = clock.Elapsed;
            return Task.FromResult(true);
        });

        await Task.WhenAll(first, second);

        Assert.True(secondStarted >= firstEnded, $"second started at {secondStarted}, first ended at {firstEnded}");
    }

    [Fact]
    public async Task ExecuteExclusiveAsync_OperationThrows_RollsBackChanges()
    {
        var connectionString = PostgresTestDatabase.RequireConnection();

        var name = $"Rollback test {Guid.NewGuid():N}";
        var role = new Role { Name = name, NormalizedName = SystemRoles.Normalize(name) };
        try
        {
            await using (var context = CreateContext(connectionString))
            {
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    new AdminOperationExecutor(context).ExecuteExclusiveAsync<bool>(
                        $"test-lock:{Guid.NewGuid():N}",
                        async cancellationToken =>
                        {
                            context.Roles.Add(role);
                            await context.SaveChangesAsync(cancellationToken);
                            throw new InvalidOperationException("Lỗi sau khi đã lưu.");
                        }));
            }

            await using var verifyContext = CreateContext(connectionString);
            Assert.False(await verifyContext.Roles.AnyAsync(entry => entry.Id == role.Id));
        }
        finally
        {
            await using var cleanupContext = CreateContext(connectionString);
            await cleanupContext.Roles.Where(entry => entry.Id == role.Id).ExecuteDeleteAsync();
        }
    }

    private static AppDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;
        return new AppDbContext(options);
    }
}
