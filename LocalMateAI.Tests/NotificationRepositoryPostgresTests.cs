using LocalMateAI.Application.DTOs.Notifications;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Repositories;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Tests;

public sealed class NotificationRepositoryPostgresTests
{
    private static readonly DateTime Now = new(2026, 10, 4, 3, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Enqueue_SameKeyTwice_InsertsOnce_AndKeepsOpenTransactionUsable()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var user = await AddUserAsync(c, "a@test.local");
        var repository = new NotificationRepository(c);
        var tripId = Guid.NewGuid();

        Assert.True(await repository.EnqueueAsync(
            Entry(user.Id, "trip-finalized:1", NotificationTargetType.Trip, tripId), Now));

        await using (var transaction = await c.Database.BeginTransactionAsync())
        {
            Assert.False(await repository.EnqueueAsync(Entry(user.Id, "trip-finalized:1"), Now));
            // Transaction vẫn dùng tiếp được sau lần trùng.
            Assert.True(await repository.EnqueueAsync(Entry(user.Id, "welcome:1"), Now));
            await transaction.CommitAsync();
        }

        var saved = await c.Notifications.AsNoTracking().OrderBy(n => n.DeduplicationKey).ToListAsync();
        Assert.Equal(["trip-finalized:1", "welcome:1"], saved.Select(n => n.DeduplicationKey));
        Assert.Equal(NotificationTargetType.Trip, saved[0].TargetType);
        Assert.Equal(tripId, saved[0].TargetId);
        Assert.Equal(NotificationTargetType.None, saved[1].TargetType);
        Assert.Null(saved[1].TargetId);
        Assert.All(saved, n =>
        {
            Assert.Null(n.ReadAt);
            Assert.Equal(Now, n.CreatedAt);
        });
    }

    [Fact]
    public async Task GetPaged_ReturnsOnlyOwnNotifications_NewestFirst_AndCanFilterUnread()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var owner = await AddUserAsync(c, "a@test.local");
        var other = await AddUserAsync(c, "b@test.local");
        var repository = new NotificationRepository(c);

        await repository.EnqueueAsync(Entry(owner.Id, "k1", title: "Cũ nhất"), Now.AddMinutes(-2));
        await repository.EnqueueAsync(Entry(owner.Id, "k2", title: "Ở giữa"), Now.AddMinutes(-1));
        await repository.EnqueueAsync(Entry(owner.Id, "k3", title: "Mới nhất"), Now);
        await repository.EnqueueAsync(Entry(other.Id, "k4", title: "Của người khác"), Now);
        await repository.MarkReadAsync(owner.Id, await IdOfAsync(c, "k2"), Now.AddMinutes(1));

        var firstPage = await repository.GetPagedAsync(owner.Id, new NotificationQuery { PageSize = 2 });

        Assert.Equal(3, firstPage.TotalCount);
        Assert.Equal(["Mới nhất", "Ở giữa"], firstPage.Items.Select(item => item.Title));
        Assert.Equal([false, true], firstPage.Items.Select(item => item.IsRead));

        var unread = await repository.GetPagedAsync(owner.Id, new NotificationQuery { UnreadOnly = true });

        Assert.Equal(["Mới nhất", "Cũ nhất"], unread.Items.Select(item => item.Title));
        Assert.Equal(2, await repository.CountUnreadAsync(owner.Id));
        Assert.Equal(1, await repository.CountUnreadAsync(other.Id));
    }

    [Fact]
    public async Task MarkRead_TouchesOnlyOwnUnreadRow_AndKeepsFirstReadTime()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var owner = await AddUserAsync(c, "a@test.local");
        var other = await AddUserAsync(c, "b@test.local");
        var repository = new NotificationRepository(c);
        await repository.EnqueueAsync(Entry(owner.Id, "k1"), Now);
        var id = await IdOfAsync(c, "k1");

        // Người khác không đánh dấu được và cũng không thấy thông báo này tồn tại.
        Assert.Equal(0, await repository.MarkReadAsync(other.Id, id, Now));
        Assert.False(await repository.ExistsAsync(other.Id, id));

        Assert.Equal(1, await repository.MarkReadAsync(owner.Id, id, Now.AddMinutes(1)));
        Assert.Equal(0, await repository.MarkReadAsync(owner.Id, id, Now.AddMinutes(5)));
        Assert.True(await repository.ExistsAsync(owner.Id, id));

        var saved = await c.Notifications.AsNoTracking().SingleAsync();
        Assert.Equal(Now.AddMinutes(1), saved.ReadAt);
    }

    [Fact]
    public async Task MarkAllRead_SkipsNotificationsCreatedAfterTheCall_AndOtherUsers()
    {
        await using var db = await IsolatedPlanDatabase.CreateAsync();
        await using var c = db.Context();
        var owner = await AddUserAsync(c, "a@test.local");
        var other = await AddUserAsync(c, "b@test.local");
        var repository = new NotificationRepository(c);
        await repository.EnqueueAsync(Entry(owner.Id, "k1"), Now.AddMinutes(-1));
        await repository.EnqueueAsync(Entry(owner.Id, "k2"), Now.AddMinutes(1));
        await repository.EnqueueAsync(Entry(other.Id, "k3"), Now.AddMinutes(-1));

        Assert.Equal(1, await repository.MarkAllReadAsync(owner.Id, Now));

        Assert.Equal(1, await repository.CountUnreadAsync(owner.Id));
        Assert.Equal(1, await repository.CountUnreadAsync(other.Id));
        Assert.Equal(0, await repository.MarkAllReadAsync(owner.Id, Now));
    }

    private static async Task<User> AddUserAsync(AppDbContext context, string email)
    {
        var user = new User
        {
            Email = email,
            FullName = email,
            PasswordHash = "hash",
            RoleId = await TestRoles.GetUserRoleIdAsync(context)
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static Task<Guid> IdOfAsync(AppDbContext context, string deduplicationKey) =>
        context.Notifications
            .Where(notification => notification.DeduplicationKey == deduplicationKey)
            .Select(notification => notification.Id)
            .SingleAsync();

    private static NotificationEntry Entry(
        Guid userId,
        string deduplicationKey,
        NotificationTargetType targetType = NotificationTargetType.None,
        Guid? targetId = null,
        string title = "Tiêu đề") =>
        new(userId, "test", title, "Nội dung", targetType, targetId, deduplicationKey);
}
