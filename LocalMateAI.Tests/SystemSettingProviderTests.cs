using LocalMateAI.Application.DTOs.Settings;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

namespace LocalMateAI.Tests;

public sealed class SystemSettingProviderTests
{
    private const string Key = SystemSettingKeys.MinActivePlacesPerStation;

    [Fact]
    public async Task NoStoredRow_ReturnsDefault()
    {
        var provider = CreateProvider(new FakeRepository());

        Assert.Equal(5, await provider.GetIntAsync(Key));
    }

    [Fact]
    public async Task StoredValue_IsReturned_EvenWithDifferentKeyCase()
    {
        var provider = CreateProvider(new FakeRepository(("stations.minactiveplacesperstation", "8")));

        Assert.Equal(8, await provider.GetIntAsync(Key));
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("5.5")]
    public async Task InvalidStoredValue_FallsBackToDefault(string stored)
    {
        var provider = CreateProvider(new FakeRepository((Key, stored)));

        Assert.Equal(5, await provider.GetIntAsync(Key));
    }

    [Fact]
    public async Task Values_AreCached_UntilInvalidated()
    {
        var repository = new FakeRepository((Key, "8"));
        var provider = CreateProvider(repository);

        await provider.GetIntAsync(Key);
        await provider.GetIntAsync(Key);
        Assert.Equal(1, repository.Reads);

        repository.Values[Key] = "9";
        provider.Invalidate();

        Assert.Equal(9, await provider.GetIntAsync(Key));
        Assert.Equal(2, repository.Reads);
    }

    [Fact]
    public async Task RepositoryFailure_IsNotCached()
    {
        var repository = new FakeRepository((Key, "8")) { FailNext = true };
        var provider = CreateProvider(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetIntAsync(Key));

        Assert.Equal(8, await provider.GetIntAsync(Key));
    }

    [Fact]
    public async Task UnknownKey_Throws()
    {
        var provider = CreateProvider(new FakeRepository());

        await Assert.ThrowsAsync<ArgumentException>(() => provider.GetIntAsync("Stations.Unknown"));
    }

    private static SystemSettingProvider CreateProvider(FakeRepository repository) =>
        new(repository, new MemoryCache(new MemoryCacheOptions()), NullLogger<SystemSettingProvider>.Instance);

    private sealed class FakeRepository(params (string Key, string Value)[] rows) : ISystemSettingRepository
    {
        public Dictionary<string, string> Values { get; } = rows.ToDictionary(row => row.Key, row => row.Value);
        public int Reads { get; private set; }
        public bool FailNext { get; set; }

        public Task<IReadOnlyList<SystemSettingRow>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            Reads++;
            if (FailNext)
            {
                FailNext = false;
                throw new InvalidOperationException("DB down");
            }

            IReadOnlyList<SystemSettingRow> result = Values
                .Select(pair => new SystemSettingRow(pair.Key, pair.Value, DateTime.UtcNow, null, null, null))
                .ToList();
            return Task.FromResult(result);
        }

        public Task UpsertAsync(string key, string value, DateTime updatedAt, Guid updatedByUserId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
