using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITripGenerationService
{
    Task<GenerateTripResult> GenerateAsync(
        Guid userId,
        TripRequestDto request,
        CancellationToken cancellationToken = default);
}
