using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Tests;

public sealed class TripDeletionServiceTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Delete_EmptyTripId_ReturnsInvalidWithoutWrite()
    {
        var repository = new FakeTripRepository(deleted: true);
        var service = new TripDeletionService(new FakeUserRepository(UserId), repository);

        var result = await service.DeleteAsync(UserId, Guid.Empty);

        Assert.Equal(DeleteTripResultStatus.InvalidTripId, result.Status);
        Assert.Equal(0, repository.DeleteCalls);
    }

    [Fact]
    public async Task Delete_NonPersistedUser_ReturnsUserNotFoundWithoutWrite()
    {
        var repository = new FakeTripRepository(deleted: true);
        var service = new TripDeletionService(new FakeUserRepository(), repository);

        var result = await service.DeleteAsync(Guid.NewGuid(), Guid.NewGuid());

        Assert.Equal(DeleteTripResultStatus.UserNotFound, result.Status);
        Assert.Equal(0, repository.DeleteCalls);
    }

    [Fact]
    public async Task Delete_OwnedActiveTrip_Succeeds()
    {
        var tripId = Guid.NewGuid();
        var repository = new FakeTripRepository(deleted: true);
        var service = new TripDeletionService(new FakeUserRepository(UserId), repository);

        var result = await service.DeleteAsync(UserId, tripId);

        Assert.Equal(DeleteTripResultStatus.Success, result.Status);
        Assert.Equal(1, repository.DeleteCalls);
        Assert.Equal(tripId, repository.LastTripId);
        Assert.Equal(UserId, repository.LastUserId);
    }

    [Fact]
    public async Task Delete_MissingForeignOrAlreadyDeletedTrip_ReturnsSameNotFound()
    {
        var repository = new FakeTripRepository(deleted: false);
        var service = new TripDeletionService(new FakeUserRepository(UserId), repository);

        var result = await service.DeleteAsync(UserId, Guid.NewGuid());

        Assert.Equal(DeleteTripResultStatus.TripNotFound, result.Status);
        Assert.Equal(1, repository.DeleteCalls);
    }

    private sealed class FakeTripRepository(bool deleted) : ITripRepository
    {
        public int DeleteCalls { get; private set; }
        public Guid? LastTripId { get; private set; }
        public Guid? LastUserId { get; private set; }

        public Task AddAsync(Trip trip, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> SoftDeleteAsync(
            Guid tripId,
            Guid userId,
            CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            LastTripId = tripId;
            LastUserId = userId;
            return Task.FromResult(deleted);
        }

        public Task<IReadOnlyList<MyTripReadModel>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Trip?> GetByIdAsync(Guid tripId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> AttachUserIfUnownedAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> FinalizeTripAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OwnedItineraryItemVisitReadModel?> GetOwnedItineraryItemVisitAsync(Guid itemId, Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> MarkItineraryItemVisitedIfEligibleAsync(Guid itemId, Guid userId, DateTimeOffset visitedAt, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Trip?> ForkTripAsync(Guid sourceTripId, Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<TripDetailReadModel?> GetOwnedDetailAsync(Guid tripId, Guid userId, CancellationToken cancellationToken = default) =>
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
}
