using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Commands;

/// <summary>
/// Business Command (BE-56): chuyển trạng thái trip Draft → Finalized.
/// Chứa toàn bộ logic nghiệp vụ (ownership, idempotency, transition guard);
/// TripService chỉ delegate — một nguồn sự thật cho luồng finalize.
/// </summary>
public sealed class FinalizeTripCommand(ITripRepository tripRepository) : IFinalizeTripCommand
{
    public async Task<FinalizeTripResult> ExecuteAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        if (tripId == Guid.Empty)
        {
            return FinalizeTripResult.InvalidTrip();
        }

        var trip = await tripRepository.GetByIdAsync(tripId, cancellationToken);
        if (trip is null || trip.UserId != userId)
        {
            return FinalizeTripResult.MissingTrip();
        }

        if (trip.Status == TripStatus.Finalized)
        {
            return FinalizeTripResult.AlreadyFinalized();
        }

        var finalized = await tripRepository.FinalizeTripAsync(tripId, userId, cancellationToken);

        return finalized
            ? FinalizeTripResult.Succeeded(new FinalizeTripResponse(tripId, TripStatus.Finalized.ToString()))
            : FinalizeTripResult.MissingTrip();
    }
}