using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITripService
{
    Task<FinalizeTripResult> FinalizeTripAsync(Guid userId, Guid tripId, FinalizeTripRequest request,
        CancellationToken ct = default) => request.FundingSource == "Normal"
            ? FinalizeTripAsync(userId, tripId, ct) : Task.FromResult(new FinalizeTripResult(FinalizeTripResultStatus.InvalidFunding));
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

    Task<VisitItineraryItemResult> MarkItineraryItemVisitedAsync(
        Guid userId,
        Guid itemId,
        CancellationToken cancellationToken = default);
}
