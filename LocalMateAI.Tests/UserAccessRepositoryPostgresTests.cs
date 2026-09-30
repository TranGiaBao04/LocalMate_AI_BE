using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

public sealed class UserAccessRepositoryPostgresTests
{
    private const string ConnectionEnvironmentVariable = "LOCALMATE_TEST_CONNECTION";

    [Fact]
    public async Task GetAsync_CustomRoleLockedUser_ReadsStatusRoleAndPermissions()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var suffix = Guid.NewGuid().ToString("N");
        var role = new Role
        {
            Name = $"Kế toán {suffix}",
            NormalizedName = SystemRoles.Normalize($"Kế toán {suffix}"),
            Permissions = [new RolePermission { Permission = Permissions.ViewRevenue }]
        };
        var userId = Guid.NewGuid();
        try
        {
            await using (var context = CreateContext(connectionString))
            {
                context.Roles.Add(role);
                context.Users.Add(new User
                {
                    Id = userId,
                    FullName = "User access test",
                    Email = $"user-access-{suffix}@localmate.test",
                    RoleId = role.Id,
                    Status = UserStatus.Locked,
                    LockedAt = DateTime.UtcNow,
                    LockReason = "Test"
                });
                await context.SaveChangesAsync();
            }

            await using var readContext = CreateContext(connectionString);
            var access = await new UserAccessRepository(readContext).GetAsync(userId);

            Assert.NotNull(access);
            Assert.Equal(UserStatus.Locked, access.Status);
            Assert.Equal(role.Name, access.RoleName);
            Assert.Equal(role.NormalizedName, access.RoleNormalizedName);
            Assert.False(access.RoleIsSystem);
            Assert.Equal([Permissions.ViewRevenue], access.StoredPermissions);
        }
        finally
        {
            await using var cleanupContext = CreateContext(connectionString);
            await cleanupContext.Users.Where(user => user.Id == userId).ExecuteDeleteAsync();
            await cleanupContext.Roles.Where(entry => entry.Id == role.Id).ExecuteDeleteAsync();
        }
    }

    [Fact]
    public async Task GetAsync_UnknownUser_ReturnsNull()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var context = CreateContext(connectionString);

        Assert.Null(await new UserAccessRepository(context).GetAsync(Guid.NewGuid()));
    }

    private static AppDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;
        return new AppDbContext(options);
    }
}
