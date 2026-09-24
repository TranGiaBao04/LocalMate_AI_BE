using LocalMateAI.Application.Commands;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class ForkTripCommandTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task Execute_ValidSource_ReturnsDraftCopy()
    {
        var sourceId = Guid.NewGuid();
        var copyId = Guid.NewGuid();
        var fake = new FakeTripRepository(
            new Trip
            {
                Id = copyId,
                UserId = UserId,
                Status = TripStatus.Draft,
                CreatedAt = DateTime.UtcNow
            });

        var command = new ForkTripCommand(fake);

        var result = await command.ExecuteAsync(UserId, sourceId);

        Assert.Equal(ForkTripResultStatus.Success, result.Status);
        Assert.NotNull(result.Response);
        Assert.Equal(copyId, result.Response.TripId);
        Assert.Equal(nameof(TripStatus.Draft), result.Response.Status);
    }

    [Fact]
    public async Task Execute_SourceMissingOrNotOwner_ReturnsNotFound()
    {
        var command = new ForkTripCommand(new FakeTripRepository(trip: null));

        var result = await command.ExecuteAsync(UserId, Guid.NewGuid());

        Assert.Equal(ForkTripResultStatus.TripNotFound, result.Status);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task Execute_EmptyTripId_ReturnsInvalid()
    {
        var command = new ForkTripCommand(new FakeTripRepository(trip: null));

        var result = await command.ExecuteAsync(UserId, Guid.Empty);

        Assert.Equal(ForkTripResultStatus.InvalidTripId, result.Status);
    }

    private sealed class FakeTripRepository(Trip? trip) : ITripRepository
    {
        public Task<Trip?> ForkTripAsync(
            Guid sourceTripId,
            Guid userId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(trip);

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
    }
}