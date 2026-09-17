using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class TripService(
    ITripRepository tripRepository,
    IUserRepository userRepository) : ITripService
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
}
