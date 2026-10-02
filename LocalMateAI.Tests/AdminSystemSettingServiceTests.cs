using LocalMateAI.Application.DTOs.Settings;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Tests;

public sealed class AdminSystemSettingServiceTests
{
    private const string Key = SystemSettingKeys.MinActivePlacesPerStation;
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 3, 0, 0, TimeSpan.Zero);
    private static readonly Guid AdminId = Guid.NewGuid();

    [Fact]
    public async Task GetAll_ReturnsEveryDefinition_WithDefaults()
    {
        var (service, _, _) = Create();

        var settings = await service.GetAllAsync();

        Assert.Equal(SystemSettingDefinitions.All.Count, settings.Count);
        var setting = Assert.Single(settings, item => item.Key == Key);
        Assert.Equal(5m, setting.Value);
        Assert.Equal(5m, setting.DefaultValue);
        Assert.Equal(1m, setting.MinValue);
        Assert.Equal(100m, setting.MaxValue);
        Assert.Equal(SystemSettingValueType.Integer, setting.ValueType);
        Assert.True(setting.IsDefault);
        Assert.Null(setting.UpdatedAt);
        Assert.Null(setting.UpdatedBy);
    }

    [Fact]
    public async Task Update_StoresCanonicalKey_InvalidatesCache_AndReturnsUpdater()
    {
        var (service, repository, provider) = Create();

        var result = await service.UpdateAsync("stations.minactiveplacesperstation",
            new UpdateSystemSettingRequest(8m), AdminId);

        Assert.Equal(AdminSystemSettingResultStatus.Success, result.Status);
        Assert.Equal("8", repository.Values[Key].Value);
        Assert.Equal(AdminId, repository.Values[Key].UpdatedBy);
        Assert.Equal(1, provider.Invalidations);
        Assert.Equal(8m, result.Response!.Value);
        Assert.False(result.Response.IsDefault);
        Assert.Equal(Now.UtcDateTime, result.Response.UpdatedAt);
        Assert.Equal(new SystemSettingUpdatedBy(AdminId, "Admin", "admin@test"), result.Response.UpdatedBy);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("5.5")]
    [InlineData("0")]
    [InlineData("101")]
    public async Task Update_InvalidValue_IsRejectedWithoutWriting(string? raw)
    {
        var (service, repository, provider) = Create();
        decimal? value = raw is null ? null : decimal.Parse(raw, System.Globalization.CultureInfo.InvariantCulture);

        var result = await service.UpdateAsync(Key, new UpdateSystemSettingRequest(value), AdminId);

        Assert.Equal(AdminSystemSettingResultStatus.InvalidValue, result.Status);
        Assert.False(string.IsNullOrEmpty(result.Error));
        Assert.Empty(repository.Values);
        Assert.Equal(0, provider.Invalidations);
    }

    [Fact]
    public async Task Update_UnknownKey_ReturnsNotFound()
    {
        var (service, repository, _) = Create();

        var result = await service.UpdateAsync("Stations.Unknown", new UpdateSystemSettingRequest(8m), AdminId);

        Assert.Equal(AdminSystemSettingResultStatus.NotFound, result.Status);
        Assert.Empty(repository.Values);
    }

    [Fact]
    public async Task Reset_DeletesRow_InvalidatesCache_AndReturnsDefault()
    {
        var (service, repository, provider) = Create();
        await service.UpdateAsync(Key, new UpdateSystemSettingRequest(8m), AdminId);

        var result = await service.ResetAsync(Key);

        Assert.Equal(AdminSystemSettingResultStatus.Success, result.Status);
        Assert.Empty(repository.Values);
        Assert.Equal(2, provider.Invalidations);
        Assert.Equal(5m, result.Response!.Value);
        Assert.True(result.Response.IsDefault);
        Assert.Null(result.Response.UpdatedBy);
    }

    [Fact]
    public async Task Reset_UnknownKey_ReturnsNotFound()
    {
        var (service, _, provider) = Create();

        var result = await service.ResetAsync("Stations.Unknown");

        Assert.Equal(AdminSystemSettingResultStatus.NotFound, result.Status);
        Assert.Equal(0, provider.Invalidations);
    }

    [Fact]
    public async Task InvalidStoredValue_IsShownAsDefault()
    {
        var (service, repository, _) = Create();
        repository.Values[Key] = ("abc", Now.UtcDateTime, AdminId);

        var setting = Assert.Single(await service.GetAllAsync(), item => item.Key == Key);

        Assert.Equal(5m, setting.Value);
        Assert.True(setting.IsDefault);
        Assert.Null(setting.UpdatedBy);
    }

    private static (AdminSystemSettingService Service, FakeRepository Repository, FakeProvider Provider) Create()
    {
        var repository = new FakeRepository();
        var provider = new FakeProvider();
        return (new AdminSystemSettingService(repository, provider, new FixedTimeProvider(Now)), repository, provider);
    }

    private sealed class FakeRepository : ISystemSettingRepository
    {
        public Dictionary<string, (string Value, DateTime UpdatedAt, Guid? UpdatedBy)> Values { get; } = [];

        public Task<IReadOnlyList<SystemSettingRow>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<SystemSettingRow> rows = Values
                .Select(pair => new SystemSettingRow(pair.Key, pair.Value.Value, pair.Value.UpdatedAt,
                    pair.Value.UpdatedBy, pair.Value.UpdatedBy is null ? null : "Admin",
                    pair.Value.UpdatedBy is null ? null : "admin@test"))
                .ToList();
            return Task.FromResult(rows);
        }

        public Task UpsertAsync(string key, string value, DateTime updatedAt, Guid updatedByUserId,
            CancellationToken cancellationToken = default)
        {
            Values[key] = (value, updatedAt, updatedByUserId);
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
        {
            Values.Remove(key);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProvider : ISystemSettingProvider
    {
        public int Invalidations { get; private set; }

        public Task<int> GetIntAsync(string key, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public void Invalidate() => Invalidations++;
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
