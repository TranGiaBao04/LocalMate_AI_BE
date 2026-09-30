using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace LocalMateAI.Tests;

[Collection(AdminAccountsCollection.Name)]
public sealed class AdminAccountSeederPostgresTests
{
    private const string Password = "Admin-Seed-Test-123";

    private readonly IPasswordHashService passwordHashService = AdminAccountSeederTests.PasswordHasher();

    [Fact]
    public async Task Seed_NewEmail_CreatesAdminAndRemovesPendingRegistration()
    {
        var connectionString = PostgresTestDatabase.RequireConnection();

        var email = NewEmail();
        try
        {
            await using (var context = CreateContext(connectionString))
            {
                context.PendingRegistrations.Add(new PendingRegistration
                {
                    Email = email,
                    FullName = "Pending",
                    PasswordHash = "hash",
                    ExpiresAt = DateTime.UtcNow.AddHours(1)
                });
                await context.SaveChangesAsync();
            }

            var logger = new ListLogger();
            await using (var context = CreateContext(connectionString))
            {
                var options = new AdminSeedOptions { Email = "  " + email.ToUpperInvariant() + " ", Password = Password };
                Assert.Equal(AdminSeedResult.Created, await AdminAccountSeeder.SeedAsync(context, passwordHashService, options, logger));
            }

            await using var verifyContext = CreateContext(connectionString);
            var admin = await verifyContext.Users.AsNoTracking().Include(user => user.Role).SingleAsync(user => user.Email == email);
            Assert.Equal(SystemRoles.AdminName, admin.RoleName);
            Assert.True(admin.Role!.IsSystem);
            Assert.Equal(AdminSeedOptions.DefaultFullName, admin.FullName);
            Assert.NotEqual(
                PasswordHashVerificationResult.Failed,
                passwordHashService.VerifyHashedPassword(admin, admin.PasswordHash!, Password));
            Assert.False(await verifyContext.PendingRegistrations.AnyAsync(pending => pending.Email == email));
            Assert.All(logger.Entries, entry => Assert.DoesNotContain(Password, entry.Message));
        }
        finally
        {
            await CleanupAsync(connectionString, email);
        }
    }

    [Fact]
    public async Task Seed_EmailAlreadyUser_DoesNotPromoteOrChangePassword()
    {
        var connectionString = PostgresTestDatabase.RequireConnection();

        var email = NewEmail();
        try
        {
            string originalHash;
            await using (var context = CreateContext(connectionString))
            {
                var user = new User
                {
                    FullName = "Existing user",
                    Email = email,
                    RoleId = await TestRoles.GetUserRoleIdAsync(context)
                };
                user.PasswordHash = originalHash = passwordHashService.HashPassword(user, "Existing-Password-1");
                context.Users.Add(user);
                await context.SaveChangesAsync();
            }

            var logger = new ListLogger();
            await using (var context = CreateContext(connectionString))
            {
                var options = new AdminSeedOptions { Email = email, Password = Password };
                Assert.Equal(AdminSeedResult.AlreadyExists, await AdminAccountSeeder.SeedAsync(context, passwordHashService, options, logger));
            }

            await using var verifyContext = CreateContext(connectionString);
            var saved = await verifyContext.Users.AsNoTracking().Include(user => user.Role).SingleAsync(user => user.Email == email);
            Assert.Equal(SystemRoles.UserName, saved.RoleName);
            Assert.Equal(originalHash, saved.PasswordHash);
            Assert.Contains(logger.Entries, entry => entry.Level == LogLevel.Warning);
        }
        finally
        {
            await CleanupAsync(connectionString, email);
        }
    }

    [Fact]
    public async Task Seed_RunTwice_SecondRunReportsAlreadyExists()
    {
        var connectionString = PostgresTestDatabase.RequireConnection();

        var email = NewEmail();
        var options = new AdminSeedOptions { Email = email, Password = Password, FullName = "Seed Twice" };
        try
        {
            await using (var context = CreateContext(connectionString))
            {
                Assert.Equal(AdminSeedResult.Created, await AdminAccountSeeder.SeedAsync(context, passwordHashService, options, new ListLogger()));
            }

            var logger = new ListLogger();
            await using (var context = CreateContext(connectionString))
            {
                Assert.Equal(AdminSeedResult.AlreadyExists, await AdminAccountSeeder.SeedAsync(context, passwordHashService, options, logger));
            }

            Assert.DoesNotContain(logger.Entries, entry => entry.Level == LogLevel.Warning);
            await using var verifyContext = CreateContext(connectionString);
            Assert.Equal(1, await verifyContext.Users.CountAsync(user => user.Email == email));
        }
        finally
        {
            await CleanupAsync(connectionString, email);
        }
    }

    private static string NewEmail() => $"admin-seed-{Guid.NewGuid():N}@localmate.test";

    private static async Task CleanupAsync(string connectionString, string email)
    {
        await using var context = CreateContext(connectionString);
        await context.Users.Where(user => user.Email == email).ExecuteDeleteAsync();
        await context.PendingRegistrations.Where(pending => pending.Email == email).ExecuteDeleteAsync();
    }

    private static AppDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;
        return new AppDbContext(options);
    }
}
