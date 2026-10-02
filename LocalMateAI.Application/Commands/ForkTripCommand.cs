using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;

namespace LocalMateAI.Application.Commands;

/// <summary>
/// BE-59: Nhân bản (fork) trip thành bản Draft mới (deep-copy items + tags).
/// Bản gốc không đổi; tuân theo guard BE-58 (suffix "fork" đã exempt).
/// </summary>
public sealed class ForkTripCommand(ITripRepository tripRepository) : IForkTripCommand
{
    public async Task<ForkTripResult> ExecuteAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        if (tripId == Guid.Empty)
        {
            return ForkTripResult.InvalidTrip();
        }

        var copy = await tripRepository.ForkTripAsync(tripId, userId, cancellationToken);
        return copy is null
            ? ForkTripResult.MissingTrip()
            : ForkTripResult.Succeeded(new ForkTripResponse(copy.Id, copy.Status.ToString()));
    }
}