using LocalMateAI.Application.DTOs.Users;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

public sealed class AdminUserLockPostgresTests
{
    [Fact]
    public async Task LockThenUnlock_PersistsStateAndLocker()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        Guid adminId, userId;
        await using (var c = db.Context())
        {
            adminId = (await AddUserAsync(c, await RoleIdAsync(c, SystemRoles.AdminName), "admin@test.dev", "Quản trị")).Id;
            userId = (await AddUserAsync(c, await RoleIdAsync(c, SystemRoles.UserName), "an@test.dev", "An")).Id;
        }

        await using (var c = db.Context())
        {
            var locked = await Service(c).LockAsync(userId, new LockUserRequest(" spam "), adminId);

            Assert.Equal(AdminUserLockResultStatus.Success, locked.Status);
            Assert.Equal("Locked", locked.Response!.Status);
            Assert.Equal("spam", locked.Response.LockReason);
            Assert.NotNull(locked.Response.LockedAt);
            Assert.Equal(new AdminUserLockedByResponse(adminId, "Quản trị", "admin@test.dev"), locked.Response.LockedBy);
        }

        await using (var c = db.Context())
        {
            var user = await c.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
            Assert.Equal(UserStatus.Locked, user.Status);
            Assert.Equal(adminId, user.LockedByUserId);

            var again = await Service(c).LockAsync(userId, new LockUserRequest("lý do khác"), adminId);
            Assert.Equal("spam", again.Response!.LockReason);
            Assert.Equal(user.LockedAt, again.Response.LockedAt);
        }

        await using (var c = db.Context())
        {
            var unlocked = await Service(c).UnlockAsync(userId, adminId);

            Assert.Equal(AdminUserLockResultStatus.Success, unlocked.Status);
            Assert.Equal(new AdminUserLockResponse(userId, "Active", null, null, null), unlocked.Response);
            var user = await c.Users.AsNoTracking().SingleAsync(u => u.Id == userId);
            Assert.Null(user.LockedByUserId);
            Assert.Null(user.LockReason);
        }
    }

    [Fact]
    public async Task TwoAdminsLockingEachOtherConcurrently_LeaveExactlyOneActiveRoleManager()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        Guid first, second;
        await using (var c = db.Context())
        {
            var adminRoleId = await RoleIdAsync(c, SystemRoles.AdminName);
            first = (await AddUserAsync(c, adminRoleId, "first@test.dev")).Id;
            second = (await AddUserAsync(c, adminRoleId, "second@test.dev")).Id;
        }

        // Mỗi request một DbContext riêng, giống 2 request HTTP song song.
        async Task<AdminUserLockResultStatus> LockAsync(Guid target, Guid actor)
        {
            await using var c = db.Context();
            return (await Service(c).LockAsync(target, new LockUserRequest("tranh chấp"), actor)).Status;
        }

        var results = await Task.WhenAll(LockAsync(second, first), LockAsync(first, second));

        // Request chạy sau (đợi khoá tuần tự) thấy người gọi vừa bị khoá ⇒ mất ManageRoles ⇒ bị từ chối.
        Assert.Single(results, status => status == AdminUserLockResultStatus.Success);
        Assert.Single(results, status => status == AdminUserLockResultStatus.CannotManageRoleManager);
        await using var check = db.Context();
        Assert.Equal(1, await new AdminRoleRepository(check).CountActiveRoleManagersAsync(null, null));
    }

    private static AdminUserService Service(AppDbContext c) => new(
        new AdminUserRepository(c),
        new AdminTransactionRepository(c),
        subscriptionService: null!,
        new AdminRoleRepository(c),
        new AdminOperationExecutor(c),
        TimeProvider.System);

    private static Task<Guid> RoleIdAsync(AppDbContext c, string roleName)
    {
        var normalizedName = SystemRoles.Normalize(roleName);
        return c.Roles.AsNoTracking()
            .Where(role => role.IsSystem && role.NormalizedName == normalizedName)
            .Select(role => role.Id)
            .SingleAsync();
    }

    private static async Task<User> AddUserAsync(AppDbContext c, Guid roleId, string email, string? fullName = null)
    {
        var user = new User { Email = email, FullName = fullName ?? email, PasswordHash = "hash", RoleId = roleId };
        c.Users.Add(user);
        await c.SaveChangesAsync();
        return user;
    }
}
