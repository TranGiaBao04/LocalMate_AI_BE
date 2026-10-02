using LocalMateAI.Application.Commands;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class TripServiceFinalizeTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task FinalizeTrip_DelegatesToCommand_AndReturnsItsResult()
    {
        var tripId = Guid.NewGuid();
        var response = new FinalizeTripResponse(tripId, nameof(TripStatus.Finalized));
        var fakeCommand = new FakeFinalizeTripCommand(
            FinalizeTripResult.Succeeded(response));

        var service = new TripService(
            new NotSupportedTripRepository(),
            new FakeUserRepository(),
            fakeCommand);

        var result = await service.FinalizeTripAsync(UserId, tripId);

        Assert.Equal(FinalizeTripResultStatus.Success, result.Status);
        Assert.Same(response, result.Response);
        Assert.Equal(UserId, fakeCommand.ReceivedUserId);
        Assert.Equal(tripId, fakeCommand.ReceivedTripId);
    }

    [Fact]
    public async Task FinalizeTrip_CommandFailure_PropagatesResult()
    {
        var fakeCommand = new FakeFinalizeTripCommand(FinalizeTripResult.MissingTrip());

        var service = new TripService(
            new NotSupportedTripRepository(),
            new FakeUserRepository(),
            fakeCommand);

        var result = await service.FinalizeTripAsync(UserId, Guid.NewGuid());

        Assert.Equal(FinalizeTripResultStatus.TripNotFound, result.Status);
        Assert.Null(result.Response);
    }

    private sealed class FakeFinalizeTripCommand(FinalizeTripResult result) : IFinalizeTripCommand
    {
        public Guid? ReceivedUserId { get; private set; }
        public Guid? ReceivedTripId { get; private set; }

        public Task<FinalizeTripResult> ExecuteAsync(
            Guid userId,
            Guid tripId,
            CancellationToken cancellationToken = default)
        {
            ReceivedUserId = userId;
            ReceivedTripId = tripId;
            return Task.FromResult(result);
        }
    }

    private sealed class NotSupportedTripRepository : ITripRepository
    {
        public Task<int> CountFinalizedByUserAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MyTripReadModel>> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Trip?> GetByIdAsync(
            Guid tripId,
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

        public Task<OwnedItineraryItemVisitReadModel?> GetOwnedItineraryItemVisitAsync(
            Guid itemId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> MarkItineraryItemVisitedIfEligibleAsync(
            Guid itemId,
            Guid userId,
            DateTimeOffset visitedAt,
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

    private sealed class FakeUserRepository : IUserRepository
    {
        public Task<bool> EmailExistsAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByEmailAsync(string email, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByIdAsync(Guid userId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<User?> GetByIdForUpdateAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TryAddAsync(User user, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpdatePasswordHashAsync(
            User user,
            string passwordHash,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveProfileChangesAsync(
            User user,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
