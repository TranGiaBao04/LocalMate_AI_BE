using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace LocalMateAI.Application.Services;

public sealed class SystemSettingProvider(
    ISystemSettingRepository repository,
    IMemoryCache cache,
    ILogger<SystemSettingProvider> logger) : ISystemSettingProvider
{
    public const string CacheKey = "system-settings";
    public static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public async Task<int> GetIntAsync(string key, CancellationToken cancellationToken = default)
    {
        var definition = SystemSettingDefinitions.Find(key)
            ?? throw new ArgumentException($"System setting '{key}' is not defined.", nameof(key));
        if (definition.ValueType != SystemSettingValueType.Integer)
        {
            throw new ArgumentException($"System setting '{key}' is not an integer.", nameof(key));
        }

        return (int)await GetValueAsync(definition, cancellationToken);
    }

    public void Invalidate() => cache.Remove(CacheKey);

    private async Task<decimal> GetValueAsync(SystemSettingDefinition definition, CancellationToken cancellationToken)
    {
        // Lỗi DB ném ra ngoài và không được cache.
        var stored = await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheDuration;
            var rows = await repository.GetAllAsync(cancellationToken);
            return rows.ToDictionary(row => row.Key, row => row.Value, StringComparer.OrdinalIgnoreCase);
        });

        if (stored!.TryGetValue(definition.Key, out var raw))
        {
            if (SystemSettingDefinitions.TryParse(definition, raw, out var value))
            {
                return value;
            }

            logger.LogWarning("Giá trị đã lưu của {SettingKey} không hợp lệ, dùng mặc định.", definition.Key);
        }

        return definition.DefaultValue;
    }
}
