using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

[Collection(AdminAccountsCollection.Name)]
public sealed class AdminRoleRepositoryPostgresTests
{
    private const string ConnectionEnvironmentVariable = "LOCALMATE_TEST_CONNECTION";

    [Fact]
    public async Task AddAndRead_RoleWithPermissionAndUser_ReturnsPermissionsAndUserCount()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var testData = new TestData();
        try
        {
            var role = NewRole($"Kế toán {testData.Suffix}", Permissions.ViewRevenue);
            await using (var context = CreateContext(connectionString))
            {
                Assert.True(await new AdminRoleRepository(context).TryAddAsync(role));
                testData.RoleIds.Add(role.Id);
                context.Users.Add(testData.NewUser(role.Id, UserStatus.Active));
                await context.SaveChangesAsync();
            }

            await using var readContext = CreateContext(connectionString);
            var repository = new AdminRoleRepository(readContext);
            var saved = await repository.GetByIdAsync(role.Id);

            Assert.NotNull(saved);
            Assert.Equal([Permissions.ViewRevenue], saved.StoredPermissions);
            Assert.Equal(1, saved.UserCount);
            Assert.True(await repository.NormalizedNameExistsAsync(role.NormalizedName, null));
            Assert.False(await repository.NormalizedNameExistsAsync(role.NormalizedName, role.Id));
        }
        finally
        {
            await testData.CleanupAsync(connectionString);
        }
    }

    [Fact]
    public async Task TryAddAsync_DuplicateNameInsideExecutor_ReturnsFalseAndTransactionStillCommits()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var testData = new TestData();
        try
        {
            var original = NewRole($"Trùng {testData.Suffix}");
            var duplicate = NewRole($"TRÙNG {testData.Suffix}");
            var another = NewRole($"Khác {testData.Suffix}");
            testData.RoleIds.AddRange([original.Id, duplicate.Id, another.Id]);

            await using (var context = CreateContext(connectionString))
            {
                var repository = new AdminRoleRepository(context);
                Assert.True(await repository.TryAddAsync(original));

                var results = await new AdminOperationExecutor(context).ExecuteExclusiveAsync(
                    $"test-lock:{testData.Suffix}",
                    async cancellationToken => (
                        Duplicate: await repository.TryAddAsync(duplicate, cancellationToken),
                        Another: await repository.TryAddAsync(another, cancellationToken)));

                Assert.False(results.Duplicate);
                Assert.True(results.Another);
            }

            await using var verifyContext = CreateContext(connectionString);
            Assert.False(await verifyContext.Roles.AnyAsync(role => role.Id == duplicate.Id));
            Assert.True(await verifyContext.Roles.AnyAsync(role => role.Id == another.Id));
        }
        finally
        {
            await testData.CleanupAsync(connectionString);
        }
    }

    [Fact]
    public async Task CountActiveRoleManagersAsync_CountsOnlyActiveManagersAndHonorsExclusions()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var testData = new TestData();
        try
        {
            var managerRole = NewRole($"Quản trị phụ {testData.Suffix}", Permissions.ManageRoles);
            var activeManager = testData.NewUser(managerRole.Id, UserStatus.Active);
            var lockedManager = testData.NewUser(managerRole.Id, UserStatus.Locked);

            int baseline;
            await using (var context = CreateContext(connectionString))
            {
                baseline = await new AdminRoleRepository(context).CountActiveRoleManagersAsync(null, null);
                context.Roles.Add(managerRole);
                testData.RoleIds.Add(managerRole.Id);
                context.Users.AddRange(activeManager, lockedManager);
                await context.SaveChangesAsync();
            }

            await using var readContext = CreateContext(connectionString);
            var repository = new AdminRoleRepository(readContext);

            Assert.Equal(baseline + 1, await repository.CountActiveRoleManagersAsync(null, null));
            Assert.Equal(baseline, await repository.CountActiveRoleManagersAsync(activeManager.Id, null));
            Assert.Equal(baseline, await repository.CountActiveRoleManagersAsync(null, managerRole.Id));
        }
        finally
        {
            await testData.CleanupAsync(connectionString);
        }
    }

    [Fact]
    public async Task SetUserRoleAsync_ChangesUserRole()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var testData = new TestData();
        try
        {
            var targetRole = NewRole($"Đích {testData.Suffix}");
            User user;
            await using (var context = CreateContext(connectionString))
            {
                user = testData.NewUser(await TestRoles.GetUserRoleIdAsync(context), UserStatus.Active);
                context.Roles.Add(targetRole);
                testData.RoleIds.Add(targetRole.Id);
                context.Users.Add(user);
                await context.SaveChangesAsync();
            }

            await using (var context = CreateContext(connectionString))
            {
                await new AdminRoleRepository(context).SetUserRoleAsync(user.Id, targetRole.Id);
            }

            await using var verifyContext = CreateContext(connectionString);
            var assignment = await new AdminRoleRepository(verifyContext).GetUserRoleAsync(user.Id);
            Assert.Equal(targetRole.Id, assignment!.RoleId);
            Assert.Equal(targetRole.NormalizedName, assignment.RoleNormalizedName);
        }
        finally
        {
            await testData.CleanupAsync(connectionString);
        }
    }

    private static Role NewRole(string name, params string[] permissions)
    {
        var role = new Role { Name = name, NormalizedName = SystemRoles.Normalize(name) };
        foreach (var permission in permissions)
        {
            role.Permissions.Add(new RolePermission { RoleId = role.Id, Permission = permission });
        }

        return role;
    }

    private static AppDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .Options;
        return new AppDbContext(options);
    }

    private sealed class TestData
    {
        public string Suffix { get; } = Guid.NewGuid().ToString("N")[..12];
        public List<Guid> RoleIds { get; } = [];
        public List<Guid> UserIds { get; } = [];

        public User NewUser(Guid roleId, UserStatus status)
        {
            var user = new User
            {
                FullName = "Admin role repository test",
                Email = $"admin-role-{Suffix}-{UserIds.Count}@localmate.test",
                RoleId = roleId,
                Status = status
            };
            UserIds.Add(user.Id);
            return user;
        }

        public async Task CleanupAsync(string connectionString)
        {
            await using var context = CreateContext(connectionString);
            await context.Users.Where(user => UserIds.Contains(user.Id)).ExecuteDeleteAsync();
            await context.Roles.Where(role => RoleIds.Contains(role.Id)).ExecuteDeleteAsync();
        }
    }
}
