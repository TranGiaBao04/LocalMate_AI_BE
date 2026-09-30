using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using Microsoft.Extensions.Caching.Memory;

namespace LocalMateAI.Tests;

public sealed class SystemRoleProviderTests
{
    [Fact]
    public async Task GetUserRoleAsync_CalledTwice_QueriesRepositoryOnce()
    {
        var repository = new FakeRoleRepository();
        var provider = new SystemRoleProvider(repository, new MemoryCache(new MemoryCacheOptions()));

        var first = await provider.GetUserRoleAsync();
        var second = await provider.GetUserRoleAsync();

        Assert.Equal(first, second);
        Assert.Equal("User", first.Name);
        Assert.Equal(["USER"], repository.RequestedNames);
    }

    [Fact]
    public async Task GetAdminRoleAsync_UsesNormalizedAdminName()
    {
        var repository = new FakeRoleRepository();
        var provider = new SystemRoleProvider(repository, new MemoryCache(new MemoryCacheOptions()));

        var admin = await provider.GetAdminRoleAsync();

        Assert.Equal("Admin", admin.Name);
        Assert.Equal(["ADMIN"], repository.RequestedNames);
    }

    [Fact]
    public async Task GetUserRoleAsync_RoleMissingInDatabase_Throws()
    {
        var repository = new FakeRoleRepository { ReturnNull = true };
        var provider = new SystemRoleProvider(repository, new MemoryCache(new MemoryCacheOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetUserRoleAsync());
    }

    private sealed class FakeRoleRepository : IRoleRepository
    {
        public bool ReturnNull { get; init; }

        public List<string> RequestedNames { get; } = [];

        public Task<SystemRoleReference?> GetSystemRoleAsync(
            string normalizedName,
            CancellationToken cancellationToken = default)
        {
            RequestedNames.Add(normalizedName);
            return Task.FromResult<SystemRoleReference?>(ReturnNull
                ? null
                : new SystemRoleReference(Guid.NewGuid(), normalizedName == "ADMIN" ? "Admin" : "User"));
        }
    }
}
