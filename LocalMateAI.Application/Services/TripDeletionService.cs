using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class TripDeletionService(
    IUserRepository userRepository,
    ITripRepository tripRepository,
    ITripFinalizeQuotaExecutor? executor = null) : ITripDeletionService
{
    public async Task<DeleteTripResult> DeleteAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        if (tripId == Guid.Empty)
        {
            return DeleteTripResult.InvalidTrip();
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return DeleteTripResult.MissingUser();
        }

        // Serialize deletion with finalize/consumption; retain consumed evidence after soft deletion.
        if (executor is not null)
        {
            var result = await executor.ExecuteForUserAsync(userId, async ct =>
                await tripRepository.LockOwnedForFinalizeAsync(tripId, userId, ct) is not null
                && await tripRepository.SoftDeleteAsync(tripId, userId, ct), cancellationToken);
            return result.PersistedUserExists && result.Result ? DeleteTripResult.Succeeded() : DeleteTripResult.MissingTrip();
        }
        return await tripRepository.SoftDeleteAsync(tripId, userId, cancellationToken)
            ? DeleteTripResult.Succeeded()
            : DeleteTripResult.MissingTrip();
    }
}
