using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Tests;

/// <summary>Provider giả: trả mặc định khai báo trong code, ghi đè được từng khoá bằng Set.</summary>
internal sealed class FakeSystemSettingProvider : ISystemSettingProvider
{
    private readonly Dictionary<string, decimal> overrides = new(StringComparer.OrdinalIgnoreCase);

    public List<string> RequestedKeys { get; } = [];
    public int Invalidations { get; private set; }

    public FakeSystemSettingProvider Set(string key, decimal value)
    {
        overrides[key] = value;
        return this;
    }

    public Task<int> GetIntAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult((int)Get(key));

    public Task<decimal> GetDecimalAsync(string key, CancellationToken cancellationToken = default) =>
        Task.FromResult(Get(key));

    public void Invalidate() => Invalidations++;

    private decimal Get(string key)
    {
        RequestedKeys.Add(key);
        var definition = SystemSettingDefinitions.Find(key) ?? throw new ArgumentException(key);
        return overrides.TryGetValue(definition.Key, out var value) ? value : definition.DefaultValue;
    }
}
