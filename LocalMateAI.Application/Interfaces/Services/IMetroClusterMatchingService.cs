using LocalMateAI.Application.DTOs.Matching;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IMetroClusterMatchingService
{
    Task<IReadOnlyList<PlaceCandidateDto>> GetCandidatesAsync(
        Guid originStationId,
        double radiusMeters,
        CancellationToken cancellationToken = default);
}