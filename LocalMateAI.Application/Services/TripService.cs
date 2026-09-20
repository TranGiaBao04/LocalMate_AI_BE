using LocalMateAI.Application.Commands;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class TripService(
    ITripRepository tripRepository,
    IUserRepository userRepository,
    IFinalizeTripCommand finalizeTripCommand) : ITripService
{
    public async Task<MyTripsResult> GetMyTripsAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return new MyTripsResult(false);
        }

        var trips = await tripRepository.GetByUserIdAsync(userId, cancellationToken);
        return new MyTripsResult(true, trips.Select(trip => new MyTripResponse(
            trip.Id,
            trip.Status.ToString(),
            trip.StartLatitude,
            trip.StartLongitude,
            trip.DurationHours,
            trip.BudgetMin,
            trip.BudgetMax,
            trip.ItemCount,
            trip.CreatedAt,
            trip.UpdatedAt)).ToArray());
    }

    public async Task<SaveTripResult> SaveTripAsync(
        Guid userId,
        SaveTripRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.TripId == Guid.Empty)
        {
            return SaveTripResult.InvalidTrip();
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return SaveTripResult.MissingUser();
        }

        var trip = await tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        if (trip is null || (trip.UserId.HasValue && trip.UserId.Value != userId))
        {
            return SaveTripResult.MissingTrip();
        }

        if (trip.UserId == userId)
        {
            return SaveTripResult.Succeeded(new SaveTripResponse(trip.Id, trip.Status.ToString()));
        }

        if (await tripRepository.AttachUserIfUnownedAsync(trip.Id, userId, cancellationToken))
        {
            return SaveTripResult.Succeeded(new SaveTripResponse(trip.Id, trip.Status.ToString()));
        }

        // Another request may have attached or removed the Trip after the first read.
        trip = await tripRepository.GetByIdAsync(request.TripId, cancellationToken);
        return trip?.UserId == userId
            ? SaveTripResult.Succeeded(new SaveTripResponse(trip.Id, trip.Status.ToString()))
            : SaveTripResult.MissingTrip();
    }

    public Task<FinalizeTripResult> FinalizeTripAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken = default) =>
        finalizeTripCommand.ExecuteAsync(userId, tripId, cancellationToken);

    public async Task<VisitItineraryItemResult> MarkItineraryItemVisitedAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        if (itemId == Guid.Empty)
        {
            return VisitItineraryItemResult.InvalidItem();
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return VisitItineraryItemResult.MissingUser();
        }

        var item = await tripRepository.GetOwnedItineraryItemVisitAsync(
            itemId,
            userId,
            cancellationToken);
        if (item is null)
        {
            return VisitItineraryItemResult.MissingItem();
        }

        if (item.TripStatus != TripStatus.Finalized)
        {
            return VisitItineraryItemResult.NotFinalized();
        }

        if (item.IsVisited)
        {
            return VisitItineraryItemResult.Succeeded(ToVisitResponse(item));
        }

        var visitedAt = DateTimeOffset.UtcNow;
        if (await tripRepository.MarkItineraryItemVisitedIfEligibleAsync(
                itemId,
                userId,
                visitedAt,
                cancellationToken))
        {
            return VisitItineraryItemResult.Succeeded(new VisitItineraryItemResponse(
                item.Id,
                item.TripId,
                true,
                visitedAt));
        }

        item = await tripRepository.GetOwnedItineraryItemVisitAsync(itemId, userId, cancellationToken);
        if (item is null)
        {
            return VisitItineraryItemResult.MissingItem();
        }

        return item.TripStatus != TripStatus.Finalized
            ? VisitItineraryItemResult.NotFinalized()
            : item.IsVisited
                ? VisitItineraryItemResult.Succeeded(ToVisitResponse(item))
                : VisitItineraryItemResult.MissingItem();
    }

    private static VisitItineraryItemResponse ToVisitResponse(OwnedItineraryItemVisitReadModel item) =>
        new(item.Id, item.TripId, item.IsVisited, item.VisitedAt);
}
