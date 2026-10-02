using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITripFeasibilityService
{
    Task<TripFeasibilityResult> CheckFeasibilityAsync(
        TripRequestDto request,
        CancellationToken cancellationToken = default);
}
