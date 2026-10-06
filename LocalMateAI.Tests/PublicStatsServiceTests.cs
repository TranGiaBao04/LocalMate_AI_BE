using LocalMateAI.Application.DTOs.PublicStats;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using Microsoft.Extensions.Caching.Memory;

namespace LocalMateAI.Tests;

public sealed class PublicStatsServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 6, 3, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Get_ReturnsFinalizedTripCount_AndServesRepeatCallsFromCache()
    {
        var repository = new FakeRepository { Count = 128 };
        var service = CreateService(repository);

        var first = await service.GetAsync();
        repository.Count = 130;
        var second = await service.GetAsync();

        Assert.Equal(new PublicStatsResponse(128, Now.UtcDateTime), first);
        Assert.Equal(first, second);
        Assert.Equal(1, repository.Calls);
    }

    [Fact]
    public async Task RepositoryFailure_IsNotCached()
    {
        var repository = new FakeRepository { Count = 7, FailNext = true };
        var service = CreateService(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetAsync());
        var result = await service.GetAsync();

        Assert.Equal(7, result.TripsFinalized);
        Assert.Equal(2, repository.Calls);
    }

    private static PublicStatsService CreateService(FakeRepository repository) =>
        new(repository, new MemoryCache(new MemoryCacheOptions()), new FixedTimeProvider(Now));

    private sealed class FakeRepository : IPublicStatsRepository
    {
        public long Count { get; set; }
        public bool FailNext { get; set; }
        public int Calls { get; private set; }

        public Task<long> CountFinalizedTripsAsync(CancellationToken cancellationToken = default)
        {
            Calls++;
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("Database unavailable.");
            }

            return Task.FromResult(Count);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
