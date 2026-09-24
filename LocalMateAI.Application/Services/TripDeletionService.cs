using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class TripDeletionService(
    IUserRepository userRepository,
    ITripRepository tripRepository) : ITripDeletionService
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

        // Không tồn tại, của người khác hoặc đã xoá đều cho cùng một kết quả để không lộ thông tin.
        return await tripRepository.SoftDeleteAsync(tripId, userId, cancellationToken)
            ? DeleteTripResult.Succeeded()
            : DeleteTripResult.MissingTrip();
    }
}
