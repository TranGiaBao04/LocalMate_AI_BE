using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

public sealed class RoleSchemaPostgresTests
{

    [Fact]
    public async Task Migration_SeedsBothSystemRoles()
    {
        var connectionString = PostgresTestDatabase.RequireConnection();

        await using var context = CreateContext(connectionString);
        var systemRoles = await context.Roles
            .AsNoTracking()
            .Where(role => role.IsSystem)
            .ToListAsync();

        Assert.Equal(2, systemRoles.Count);
        Assert.Contains(systemRoles, role => role.Name == SystemRoles.UserName && role.NormalizedName == "USER");
        Assert.Contains(systemRoles, role => role.Name == SystemRoles.AdminName && role.NormalizedName == "ADMIN");
    }

    [Fact]
    public async Task NewUserWithUserRole_IsActiveAndLoadsRoleName()
    {
        var connectionString = PostgresTestDatabase.RequireConnection();

        var userId = Guid.NewGuid();
        try
        {
            await using (var context = CreateContext(connectionString))
            {
                context.Users.Add(new User
                {
                    Id = userId,
                    FullName = "Role schema test",
                    Email = $"role-schema-{userId:N}@localmate.test",
                    RoleId = await TestRoles.GetUserRoleIdAsync(context)
                });
                await context.SaveChangesAsync();
            }

            await using var verifyContext = CreateContext(connectionString);
            var saved = await verifyContext.Users
                .AsNoTracking()
                .Include(user => user.Role)
                .SingleAsync(user => user.Id == userId);

            Assert.Equal(SystemRoles.UserName, saved.RoleName);
            Assert.Equal(UserStatus.Active, saved.Status);
            Assert.Null(saved.LockedAt);
            Assert.Null(saved.LockReason);
        }
        finally
        {
            await CleanupAsync(connectionString, userId, roleId: null);
        }
    }

    [Fact]
    public async Task UserWithoutRole_IsRejectedByForeignKey()
    {
        var connectionString = PostgresTestDatabase.RequireConnection();

        var userId = Guid.NewGuid();
        try
        {
            await using var context = CreateContext(connectionString);
            context.Users.Add(new User
            {
                Id = userId,
                FullName = "Missing role test",
                Email = $"missing-role-{userId:N}@localmate.test"
            });

            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
        finally
        {
            await CleanupAsync(connectionString, userId, roleId: null);
        }
    }

    [Fact]
    public async Task DeletingRoleInUse_IsRejectedByForeignKey()
    {
        var connectionString = PostgresTestDatabase.RequireConnection();

        var suffix = Guid.NewGuid().ToString("N");
        var role = new Role
        {
            Name = $"Test role {suffix}",
            NormalizedName = SystemRoles.Normalize($"Test role {suffix}")
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
                    FullName = "Role in use test",
                    Email = $"role-in-use-{suffix}@localmate.test",
                    RoleId = role.Id
                });
                await context.SaveChangesAsync();
            }

            await using var deleteContext = CreateContext(connectionString);
            await Assert.ThrowsAsync<DbUpdateException>(async () =>
            {
                deleteContext.Roles.Remove(await deleteContext.Roles.SingleAsync(entry => entry.Id == role.Id));
                await deleteContext.SaveChangesAsync();
            });
        }
        finally
        {
            await CleanupAsync(connectionString, userId, role.Id);
        }
    }

    private static async Task CleanupAsync(string connectionString, Guid userId, Guid? roleId)
    {
        await using var context = CreateContext(connectionString);
        await context.Users.Where(user => user.Id == userId).ExecuteDeleteAsync();
        if (roleId is not null)
        {
            await context.Roles.Where(role => role.Id == roleId).ExecuteDeleteAsync();
        }
    }

    private static AppDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;
        return new AppDbContext(options);
    }
}
