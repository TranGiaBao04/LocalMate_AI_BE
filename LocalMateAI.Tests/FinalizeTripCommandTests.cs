using LocalMateAI.Application.Commands;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class FinalizeTripCommandTests
{
    private static readonly Guid UserId = Guid.NewGuid();
    private static readonly Guid OtherUserId = Guid.NewGuid();

    [Fact]
    public async Task Execute_DraftOwnedTrip_Succeeds()
    {
        var trip = Trip(status: TripStatus.Draft, userId: UserId);
        var command = new FinalizeTripCommand(
            new FakeTripRepository(trip, finalizeResult: true));

        var result = await command.ExecuteAsync(UserId, trip.Id);

        Assert.Equal(FinalizeTripResultStatus.Success, result.Status);
        Assert.NotNull(result.Response);
        Assert.Equal(trip.Id, result.Response.TripId);
        Assert.Equal(nameof(TripStatus.Finalized), result.Response.Status);
    }

    [Fact]
    public async Task Execute_NullTrip_ReturnsNotFound()
    {
        var command = new FinalizeTripCommand(new FakeTripRepository(trip: null));

        var result = await command.ExecuteAsync(UserId, Guid.NewGuid());

        Assert.Equal(FinalizeTripResultStatus.TripNotFound, result.Status);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task Execute_TripOwnedByOtherUser_ReturnsNotFound()
    {
        var trip = Trip(status: TripStatus.Draft, userId: OtherUserId);
        var command = new FinalizeTripCommand(new FakeTripRepository(trip));

        var result = await command.ExecuteAsync(UserId, trip.Id);

        // Không lộ thông tin trip của người khác — khớp pattern SaveTrip.MissingTrip
        Assert.Equal(FinalizeTripResultStatus.TripNotFound, result.Status);
    }

    [Fact]
    public async Task Execute_AlreadyFinalized_ReturnsAlreadyFinalized()
    {
        var trip = Trip(status: TripStatus.Finalized, userId: UserId);
        var command = new FinalizeTripCommand(new FakeTripRepository(trip));

        var result = await command.ExecuteAsync(UserId, trip.Id);

        Assert.Equal(FinalizeTripResultStatus.AlreadyFinalized, result.Status);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task Execute_EmptyTripId_ReturnsInvalid()
    {
        var command = new FinalizeTripCommand(new FakeTripRepository(trip: null));

        var result = await command.ExecuteAsync(UserId, Guid.Empty);

        Assert.Equal(FinalizeTripResultStatus.InvalidTripId, result.Status);
    }

    [Fact]
    public async Task Execute_ConcurrentFinalizeLosesRace_ReturnsNotFound()
    {
        // Dòng chuyển trạng thái không thành công (đã bị request khác finalize trước) → MissingTrip
        var trip = Trip(status: TripStatus.Draft, userId: UserId);
        var command = new FinalizeTripCommand(
            new FakeTripRepository(trip, finalizeResult: false));

        var result = await command.ExecuteAsync(UserId, trip.Id);

        Assert.Equal(FinalizeTripResultStatus.TripNotFound, result.Status);
    }

    private static Trip Trip(TripStatus status, Guid userId) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            StartLatitude = 10.77,
            StartLongitude = 106.69,
            DurationHours = 3,
            BudgetMin = 0,
            BudgetMax = 300_000,
            Status = status
        };

    private sealed class FakeTripRepository(Trip? trip, bool finalizeResult = true) : ITripRepository
    {
        public Task<Trip?> GetByIdAsync(Guid tripId, CancellationToken cancellationToken = default) =>
            Task.FromResult(trip);

        public Task<bool> FinalizeTripAsync(
            Guid tripId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(finalizeResult);

        public Task<IReadOnlyList<MyTripReadModel>> GetByUserIdAsync(
            Guid userId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> AttachUserIfUnownedAsync(
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
    }
}