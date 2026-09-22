using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Tests;

public sealed class DevelopmentUserSeederTests
{
    private readonly IPasswordHashService passwordHashService = new AspNetCorePasswordHashService(
        Options.Create(new PasswordHasherOptions
        {
            CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3,
            IterationCount = 100_000
        }));

    [Fact]
    public void DevelopmentUsers_ContainsExactlyOneAdminAndThreeNormalUsers()
    {
        var users = DataSeeder.DevelopmentUsers;

        Assert.Equal(4, users.Count);

        var admin = Assert.Single(users, u => u.Role == UserRole.Admin);
        Assert.Equal("admin@localmate.dev", admin.Email);
        Assert.Equal("LocalMate Admin", admin.FullName);

        var normalUsers = users.Where(u => u.Role == UserRole.User).ToList();
        Assert.Equal(3, normalUsers.Count);

        Assert.Contains(normalUsers, u => u.Email == "user1@localmate.dev" && u.FullName == "LocalMate User 1");
        Assert.Contains(normalUsers, u => u.Email == "user2@localmate.dev" && u.FullName == "LocalMate User 2");
        Assert.Contains(normalUsers, u => u.Email == "user3@localmate.dev" && u.FullName == "LocalMate User 3");
    }

    [Fact]
    public void DevelopmentDefaultPassword_MatchesSpecification()
    {
        Assert.Equal("LocalMate@123", DataSeeder.DevelopmentDefaultPassword);
    }

    [Fact]
    public void PasswordHashing_ProducesValidHash_AndVerifiesWithCanonicalPassword()
    {
        foreach (var seed in DataSeeder.DevelopmentUsers)
        {
            var user = new User
            {
                Id = Guid.NewGuid(),
                FullName = seed.FullName,
                Email = seed.Email,
                Role = seed.Role
            };

            var hash = passwordHashService.HashPassword(user, DataSeeder.DevelopmentDefaultPassword);

            Assert.NotNull(hash);
            Assert.NotEqual(DataSeeder.DevelopmentDefaultPassword, hash);

            var verification = passwordHashService.VerifyHashedPassword(
                user,
                hash,
                DataSeeder.DevelopmentDefaultPassword);

            Assert.Equal(PasswordHashVerificationResult.Success, verification);

            var wrongVerification = passwordHashService.VerifyHashedPassword(
                user,
                hash,
                "WrongPassword!456");

            Assert.Equal(PasswordHashVerificationResult.Failed, wrongVerification);
        }
    }

    [Fact]
    public void Idempotency_SkipsExistingAccounts_CaseInsensitively()
    {
        var existingEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "admin@localmate.dev",
            "USER1@LOCALMATE.DEV"
        };

        var toAdd = DataSeeder.DevelopmentUsers
            .Where(seed => !existingEmails.Contains(seed.Email))
            .ToList();

        Assert.Equal(2, toAdd.Count);
        Assert.Contains(toAdd, u => u.Email == "user2@localmate.dev");
        Assert.Contains(toAdd, u => u.Email == "user3@localmate.dev");
        Assert.DoesNotContain(toAdd, u => u.Email == "admin@localmate.dev");
        Assert.DoesNotContain(toAdd, u => u.Email == "user1@localmate.dev");
    }
}
