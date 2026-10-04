using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Notifications;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Validators.Notifications;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class NotificationServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 4, 3, 0, 0, TimeSpan.Zero);
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task InvalidQuery_ReturnsFieldErrors_WithoutQuerying()
    {
        var repository = new FakeNotificationRepository();

        var result = await Service(repository).GetNotificationsAsync(
            UserId, new NotificationQuery { Page = 0, PageSize = 101, SortBy = "title" });

        Assert.Equal(NotificationListResultStatus.InvalidQuery, result.Status);
        Assert.Null(result.Response);
        Assert.Equal(["Page", "PageSize", "SortBy"], result.ValidationErrors!.Keys.Order());
        Assert.Empty(repository.PagedCalls);
    }

    [Fact]
    public async Task ValidQuery_ReturnsThePageOfTheCallingUser()
    {
        var item = new NotificationResponse(Guid.NewGuid(), "trip_finalized", "Tiêu đề", "Nội dung",
            NotificationTargetType.Trip, Guid.NewGuid(), false, null, Now.UtcDateTime);
        var repository = new FakeNotificationRepository
        {
            Page = PagedResult<NotificationResponse>.Create([item], 1, 20, 1)
        };
        var query = new NotificationQuery { UnreadOnly = true };

        var result = await Service(repository).GetNotificationsAsync(UserId, query);

        Assert.Equal(NotificationListResultStatus.Success, result.Status);
        Assert.Equal(item, Assert.Single(result.Response!.Items));
        Assert.Equal((UserId, query), Assert.Single(repository.PagedCalls));
    }

    [Fact]
    public async Task UnreadCount_ComesFromRepository()
    {
        var repository = new FakeNotificationRepository { UnreadCount = 7 };

        var result = await Service(repository).GetUnreadCountAsync(UserId);

        Assert.Equal(7, result.Count);
    }

    [Fact]
    public async Task MarkRead_WhenARowChanges_ReturnsTrue_WithoutExistenceCheck()
    {
        var repository = new FakeNotificationRepository { MarkReadResult = 1 };
        var id = Guid.NewGuid();

        Assert.True(await Service(repository).MarkReadAsync(UserId, id));

        Assert.Equal((UserId, id, Now.UtcDateTime), Assert.Single(repository.MarkReadCalls));
        Assert.Empty(repository.ExistsCalls);
    }

    [Fact]
    public async Task MarkRead_AlreadyRead_StillReturnsTrue()
    {
        var repository = new FakeNotificationRepository { MarkReadResult = 0, Exists = true };
        var id = Guid.NewGuid();

        Assert.True(await Service(repository).MarkReadAsync(UserId, id));

        Assert.Equal((UserId, id), Assert.Single(repository.ExistsCalls));
    }

    [Fact]
    public async Task MarkRead_MissingOrOwnedBySomeoneElse_ReturnsFalse()
    {
        var repository = new FakeNotificationRepository { MarkReadResult = 0, Exists = false };

        Assert.False(await Service(repository).MarkReadAsync(UserId, Guid.NewGuid()));
    }

    [Fact]
    public async Task MarkAllRead_UsesTheCurrentTime()
    {
        var repository = new FakeNotificationRepository();

        await Service(repository).MarkAllReadAsync(UserId);

        Assert.Equal((UserId, Now.UtcDateTime), Assert.Single(repository.MarkAllCalls));
    }

    private static NotificationService Service(FakeNotificationRepository repository) =>
        new(repository, new NotificationQueryValidator(), new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeNotificationRepository : INotificationRepository
    {
        public PagedResult<NotificationResponse> Page { get; init; } =
            PagedResult<NotificationResponse>.Create([], 1, 20, 0);
        public int UnreadCount { get; init; }
        public int MarkReadResult { get; init; }
        public bool Exists { get; init; }
        public List<(Guid UserId, NotificationQuery Query)> PagedCalls { get; } = [];
        public List<(Guid UserId, Guid Id, DateTime Now)> MarkReadCalls { get; } = [];
        public List<(Guid UserId, Guid Id)> ExistsCalls { get; } = [];
        public List<(Guid UserId, DateTime Now)> MarkAllCalls { get; } = [];

        public Task<bool> EnqueueAsync(NotificationEntry entry, DateTime now,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<PagedResult<NotificationResponse>> GetPagedAsync(Guid userId, NotificationQuery query,
            CancellationToken cancellationToken = default)
        {
            PagedCalls.Add((userId, query));
            return Task.FromResult(Page);
        }

        public Task<int> CountUnreadAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(UnreadCount);

        public Task<bool> ExistsAsync(Guid userId, Guid notificationId,
            CancellationToken cancellationToken = default)
        {
            ExistsCalls.Add((userId, notificationId));
            return Task.FromResult(Exists);
        }

        public Task<int> MarkReadAsync(Guid userId, Guid notificationId, DateTime now,
            CancellationToken cancellationToken = default)
        {
            MarkReadCalls.Add((userId, notificationId, now));
            return Task.FromResult(MarkReadResult);
        }

        public Task<int> MarkAllReadAsync(Guid userId, DateTime now, CancellationToken cancellationToken = default)
        {
            MarkAllCalls.Add((userId, now));
            return Task.FromResult(0);
        }
    }
}
