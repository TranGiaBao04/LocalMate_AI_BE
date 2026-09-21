using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Tests;

public sealed class TripItemDeletionServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid ItemId = Guid.NewGuid();

    [Fact]
    public async Task DeleteAsync_EmptyTripId_ReturnsInvalidId()
    {
        var (sut, _) = Create();

        var result = await sut.DeleteAsync(UserId, Guid.Empty, ItemId);

        Assert.Equal(DeleteItineraryItemResultStatus.InvalidId, result.Status);
    }

    [Fact]
    public async Task DeleteAsync_EmptyItemId_ReturnsInvalidId()
    {
        var (sut, _) = Create();

        var result = await sut.DeleteAsync(UserId, TripId, Guid.Empty);

        Assert.Equal(DeleteItineraryItemResultStatus.InvalidId, result.Status);
    }

    [Fact]
    public async Task DeleteAsync_UserNotPersisted_ReturnsUserNotFound()
    {
        var (sut, _) = Create(userId: Guid.NewGuid());

        var result = await sut.DeleteAsync(UserId, TripId, ItemId);

        Assert.Equal(DeleteItineraryItemResultStatus.UserNotFound, result.Status);
    }

    [Theory]
    [InlineData(DeleteItineraryItemPersistenceStatus.NotFound, DeleteItineraryItemResultStatus.ItemNotFound)]
    [InlineData(DeleteItineraryItemPersistenceStatus.TripFinalized, DeleteItineraryItemResultStatus.TripFinalized)]
    [InlineData(DeleteItineraryItemPersistenceStatus.LastItem, DeleteItineraryItemResultStatus.LastItem)]
    public async Task DeleteAsync_PersistenceRejects_MapsToServiceStatus(
        DeleteItineraryItemPersistenceStatus persistenceStatus,
        DeleteItineraryItemResultStatus expected)
    {
        var (sut, _) = Create(persistence: new DeleteItineraryItemPersistenceResult(persistenceStatus));

        var result = await sut.DeleteAsync(UserId, TripId, ItemId);

        Assert.Equal(expected, result.Status);
        Assert.Null(result.RemainingItems);
    }

    [Fact]
    public async Task DeleteAsync_Deleted_ReturnsRemainingItems()
    {
        var remaining = new List<ItineraryTimelineItemResponse>
        {
            new(Guid.NewGuid(), Guid.NewGuid(), "Place", 0, new TimeOnly(8, 0), 90, 100)
        };
        var (sut, _) = Create(persistence: new DeleteItineraryItemPersistenceResult(
            DeleteItineraryItemPersistenceStatus.Deleted,
            remaining));

        var result = await sut.DeleteAsync(UserId, TripId, ItemId);

        Assert.Equal(DeleteItineraryItemResultStatus.Success, result.Status);
        Assert.Same(remaining, result.RemainingItems);
    }

    [Fact]
    public async Task DeleteAsync_PassesTimelineRecalculatorToRepository()
    {
        var first = new TimelineItemSnapshot(Guid.NewGuid(), 0, new TimeOnly(8, 0), 90);
        var third = new TimelineItemSnapshot(Guid.NewGuid(), 2, new TimeOnly(11, 0), 90);
        var (sut, items) = Create(snapshotsToRecalculate: [first, third]);

        await sut.DeleteAsync(UserId, TripId, ItemId);

        Assert.Equal(
            new[] { new TimelineItemUpdate(third.ItemId, 1, new TimeOnly(9, 30)) },
            items.RecalculatedUpdates!.ToArray());
    }

    private static (TripItemDeletionService Sut, FakeItineraryItemRepository Items) Create(
        Guid? userId = null,
        DeleteItineraryItemPersistenceResult? persistence = null,
        IReadOnlyList<TimelineItemSnapshot>? snapshotsToRecalculate = null)
    {
        var items = new FakeItineraryItemRepository(
            persistence ?? new DeleteItineraryItemPersistenceResult(
                DeleteItineraryItemPersistenceStatus.Deleted,
                []),
            snapshotsToRecalculate ?? []);
        var sut = new TripItemDeletionService(
            new FakeUserRepository(userId ?? UserId),
            items,
            new ItineraryTimelineRecalculator());

        return (sut, items);
    }

    private sealed class FakeUserRepository(Guid userId) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid requestedUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(userId == requestedUserId ? new User { Id = requestedUserId } : null);

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByIdForUpdateAsync(Guid requestedUserId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdatePasswordHashAsync(User user, string passwordHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveProfileChangesAsync(User user, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeItineraryItemRepository(
        DeleteItineraryItemPersistenceResult persistence,
        IReadOnlyList<TimelineItemSnapshot> snapshotsToRecalculate) : IItineraryItemRepository
    {
        public IReadOnlyList<TimelineItemUpdate>? RecalculatedUpdates { get; private set; }

        public Task<DeleteItineraryItemPersistenceResult> DeleteItemAndRecalculateTimelineAsync(
            Guid tripId,
            Guid itemId,
            Guid userId,
            Func<IReadOnlyList<TimelineItemSnapshot>, IReadOnlyList<TimelineItemUpdate>> recalculateTimeline,
            CancellationToken cancellationToken = default)
        {
            RecalculatedUpdates = recalculateTimeline(snapshotsToRecalculate);
            return Task.FromResult(persistence);
        }

        public Task<OwnedItemAlternativesReadModel?> GetOwnedItemForAlternativesAsync(
            Guid tripId,
            Guid itemId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ReplacedItemReadModel?> ReplaceItemPlaceIfEligibleAsync(
            Guid tripId,
            Guid itemId,
            Guid userId,
            Guid newPlaceId,
            decimal newEstimatedBudget,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
