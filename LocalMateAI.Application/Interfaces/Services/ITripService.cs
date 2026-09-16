using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITripService
{
    Task<SaveTripResult> SaveTripAsync(
        Guid userId,
        SaveTripRequest request,
        CancellationToken cancellationToken = default);
}
