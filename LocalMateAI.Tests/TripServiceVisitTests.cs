using LocalMateAI.Application.Commands;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class TripServiceVisitTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();
    private static readonly IFinalizeTripCommand UnusedFinalizeCommand = new FakeFinalizeTripCommand();

    [Fact]
    public async Task MarkVisited_FinalizedOwnedUnvisitedItem_SucceedsWithUtcTimestamp()
    {
        var item = VisitItem(TripStatus.Finalized);
        var repository = new FakeTripRepository(item) { MarkResult = true };
        var service = new TripService(repository, new FakeUserRepository(UserId), UnusedFinalizeCommand);

        var before = DateTimeOffset.UtcNow;
        var result = await service.MarkItineraryItemVisitedAsync(UserId, item.Id);
        var after = DateTimeOffset.UtcNow;

        Assert.Equal(VisitItineraryItemResultStatus.Success, result.Status);
        Assert.NotNull(result.Response);
        Assert.True(result.Response.IsVisited);
        Assert.Equal(item.Id, result.Response.Id);
        Assert.Equal(item.TripId, result.Response.TripId);
        Assert.NotNull(result.Response.VisitedAt);
        Assert.InRange(result.Response.VisitedAt!.Value, before, after);
        Assert.Equal(TimeSpan.Zero, result.Response.VisitedAt.Value.Offset);
        Assert.Equal(1, repository.MarkCalls);
        Assert.Equal(TripStatus.Finalized, item.TripStatus);
    }

    [Fact]
    public async Task MarkVisited_AlreadyVisitedItem_ReturnsOriginalTimestampWithoutWrite()
    {
        var timestamp = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero);
        var item = VisitItem(TripStatus.Finalized, isVisited: true, visitedAt: timestamp);
        var repository = new FakeTripRepository(item);
        var service = new TripService(repository, new FakeUserRepository(UserId), UnusedFinalizeCommand);

        var result = await service.MarkItineraryItemVisitedAsync(UserId, item.Id);

        Assert.Equal(VisitItineraryItemResultStatus.Success, result.Status);
        Assert.Equal(timestamp, result.Response!.VisitedAt);
        Assert.Equal(0, repository.MarkCalls);
        Assert.Equal(item.UpdatedAt, repository.LastObservedUpdatedAt);
    }

    [Fact]
    public async Task MarkVisited_NonPersistedIdentity_IsForbiddenBeforeItemLookup()
    {
        var repository = new FakeTripRepository();
        var service = new TripService(repository, new FakeUserRepository(), UnusedFinalizeCommand);

        var result = await service.MarkItineraryItemVisitedAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(VisitItineraryItemResultStatus.UserNotFound, result.Status);
        Assert.Equal(0, repository.ReadCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MarkVisited_MissingOrForeignItem_ReturnsSameNotFoundResult(bool foreign)
    {
        var item = foreign ? VisitItem(TripStatus.Finalized) : null;
        var repository = new FakeTripRepository(item, foreign ? OtherUserId : UserId);
        var service = new TripService(repository, new FakeUserRepository(UserId), UnusedFinalizeCommand);

        var result = await service.MarkItineraryItemVisitedAsync(UserId, item?.Id ?? Guid.NewGuid());

        Assert.Equal(VisitItineraryItemResultStatus.ItemNotFound, result.Status);
        Assert.Equal(0, repository.MarkCalls);
    }

    [Fact]
    public async Task MarkVisited_OwnedDraftItem_ReturnsNotFinalizedWithoutWrite()
    {
        var item = VisitItem(TripStatus.Draft);
        var repository = new FakeTripRepository(item);
        var service = new TripService(repository, new FakeUserRepository(UserId), UnusedFinalizeCommand);

        var result = await service.MarkItineraryItemVisitedAsync(UserId, item.Id);

        Assert.Equal(VisitItineraryItemResultStatus.TripNotFinalized, result.Status);
        Assert.Equal(0, repository.MarkCalls);
        Assert.Equal(TripStatus.Draft, item.TripStatus);
    }

    [Fact]
    public async Task MarkVisited_ConditionalUpdateRace_ReturnsWinnerTimestampWithoutSecondWrite()
    {
        var timestamp = new DateTimeOffset(2026, 9, 19, 13, 0, 0, TimeSpan.Zero);
        var item = VisitItem(TripStatus.Finalized);
        var repository = new FakeTripRepository(item)
        {
            MarkResult = false,
            ReadAfterMark = item with { IsVisited = true, VisitedAt = timestamp }
        };
        var service = new TripService(repository, new FakeUserRepository(UserId), UnusedFinalizeCommand);

        var result = await service.MarkItineraryItemVisitedAsync(UserId, item.Id);

        Assert.Equal(VisitItineraryItemResultStatus.Success, result.Status);
        Assert.Equal(timestamp, result.Response!.VisitedAt);
        Assert.Equal(1, repository.MarkCalls);
        Assert.Equal(2, repository.ReadCalls);
    }

    [Fact]
    public async Task MarkVisited_EmptyItemId_ReturnsInvalidWithoutLookup()
    {
        var repository = new FakeTripRepository();
        var service = new TripService(repository, new FakeUserRepository(UserId), UnusedFinalizeCommand);

        var result = await service.MarkItineraryItemVisitedAsync(UserId, Guid.Empty);

        Assert.Equal(VisitItineraryItemResultStatus.InvalidItemId, result.Status);
        Assert.Equal(0, repository.ReadCalls);
    }

    private static OwnedItineraryItemVisitReadModel VisitItem(
        TripStatus tripStatus,
        bool isVisited = false,
        DateTimeOffset? visitedAt = null) =>
        new(
            Guid.NewGuid(),
            Guid.NewGuid(),
            tripStatus,
            isVisited,
            visitedAt,
            new DateTime(2026, 9, 19, 11, 0, 0, DateTimeKind.Utc));

    private sealed class FakeTripRepository(
        OwnedItineraryItemVisitReadModel? item = null,
        Guid? ownerId = null) : ITripRepository
    {
        private readonly Guid owningUserId = ownerId ?? UserId;

        public bool MarkResult { get; init; } = true;
        public OwnedItineraryItemVisitReadModel? ReadAfterMark { get; init; }
        public int ReadCalls { get; private set; }
        public int MarkCalls { get; private set; }
        public DateTime LastObservedUpdatedAt => item?.UpdatedAt ?? default;

        public Task<OwnedItineraryItemVisitReadModel?> GetOwnedItineraryItemVisitAsync(
            Guid itemId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            var current = ReadCalls > 1 && ReadAfterMark is not null ? ReadAfterMark : item;
            return Task.FromResult(current?.Id == itemId && userId == owningUserId ? current : null);
        }

        public Task<bool> MarkItineraryItemVisitedIfEligibleAsync(
            Guid itemId,
            Guid userId,
            DateTimeOffset visitedAt,
            CancellationToken cancellationToken = default)
        {
            MarkCalls++;
            return Task.FromResult(MarkResult);
        }

        public Task<Trip?> GetByIdAsync(Guid tripId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MyTripReadModel>> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> AttachUserIfUnownedAsync(
            Guid tripId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> FinalizeTripAsync(
            Guid tripId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Trip?> ForkTripAsync(
            Guid sourceTripId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TripDetailReadModel?> GetOwnedDetailAsync(
            Guid tripId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddAsync(
            Trip trip,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> SoftDeleteAsync(
            Guid tripId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeUserRepository(Guid? userId = null) : IUserRepository
    {
        public Task<User?> GetByIdAsync(Guid requestedUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(userId == requestedUserId ? new User { Id = requestedUserId } : null);

        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByIdForUpdateAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdatePasswordHashAsync(User user, string passwordHash, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveProfileChangesAsync(User user, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeFinalizeTripCommand : IFinalizeTripCommand
    {
        public Task<FinalizeTripResult> ExecuteAsync(
            Guid userId,
            Guid tripId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}