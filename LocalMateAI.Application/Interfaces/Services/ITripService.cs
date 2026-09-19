using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITripService
{
    Task<MyTripsResult> GetMyTripsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<SaveTripResult> SaveTripAsync(
        Guid userId,
        SaveTripRequest request,
        CancellationToken cancellationToken = default);

    Task<FinalizeTripResult> FinalizeTripAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken = default);
}