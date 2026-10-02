using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITripMatchingService
{
    Task<TripMatchingResult> MatchAsync(
        TripRequestDto request,
        CancellationToken cancellationToken = default);
}