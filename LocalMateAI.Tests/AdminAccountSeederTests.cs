using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Tests;

/// <summary>BE-81: các nhánh cấu hình thiếu/sai trả kết quả trước khi chạm DB, nên không cần DB thật.</summary>
public sealed class AdminAccountSeederTests
{
    private const string SecretPassword = "Sieu-Bi-Mat-123";

    [Fact]
    public async Task Seed_NothingConfigured_ReturnsNotConfigured()
    {
        var logger = new ListLogger();

        var result = await SeedAsync(new AdminSeedOptions(), logger);

        Assert.Equal(AdminSeedResult.NotConfigured, result);
        Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Information);
    }

    [Theory]
    [InlineData("admin@localmate.invalid", "")]
    [InlineData("", SecretPassword)]
    [InlineData("khong-phai-email", SecretPassword)]
    [InlineData("admin@localmate.invalid", "1234567")]
    [InlineData("admin@localmate.invalid", "          ")]
    public async Task Seed_InvalidConfiguration_LogsErrorWithoutPassword(string email, string password)
    {
        var logger = new ListLogger();

        var result = await SeedAsync(new AdminSeedOptions { Email = email, Password = password }, logger);

        Assert.Equal(AdminSeedResult.InvalidConfiguration, result);
        var error = Assert.Single(logger.Entries, entry => entry.Level == LogLevel.Error);
        Assert.DoesNotContain(SecretPassword, error.Message, StringComparison.Ordinal);
        if (!string.IsNullOrWhiteSpace(password))
        {
            Assert.All(logger.Entries, entry => Assert.DoesNotContain(password, entry.Message, StringComparison.Ordinal));
        }
    }

    [Fact]
    public async Task Seed_FullNameTooLong_ReturnsInvalidConfiguration()
    {
        var options = new AdminSeedOptions
        {
            Email = "admin@localmate.invalid",
            Password = SecretPassword,
            FullName = new string('a', 201)
        };

        Assert.Equal(AdminSeedResult.InvalidConfiguration, await SeedAsync(options, new ListLogger()));
    }

    private static async Task<AdminSeedResult> SeedAsync(AdminSeedOptions options, ILogger logger)
    {
        // Không mở kết nối: các nhánh này trả về trước khi truy vấn DB.
        var dbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=unused.invalid;Database=unused", npgsql => npgsql.UseNetTopologySuite())
            .Options;
        await using var context = new AppDbContext(dbOptions);

        return await AdminAccountSeeder.SeedAsync(context, PasswordHasher(), options, logger);
    }

    internal static IPasswordHashService PasswordHasher() =>
        new AspNetCorePasswordHashService(Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = 100_000
        }));
}

internal sealed class ListLogger : ILogger
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter) =>
        Entries.Add((logLevel, formatter(state, exception)));
}
