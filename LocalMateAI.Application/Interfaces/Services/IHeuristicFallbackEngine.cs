using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IHeuristicFallbackEngine
{
    Task<FallbackItineraryResult> GenerateFallbackAsync(
        TripRequestDto request,
        string fallbackReason,
        CancellationToken cancellationToken = default);
}