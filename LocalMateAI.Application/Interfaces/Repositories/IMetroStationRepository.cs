using LocalMateAI.Application.DTOs.Geo;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IMetroStationRepository
{
    Task<NearestStationResult?> FindNearestAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default);
}
