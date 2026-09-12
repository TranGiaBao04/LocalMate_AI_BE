using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.MasterData;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IMetroStationRepository
{
    Task<NearestStationResult?> FindNearestAsync(
        double latitude,
        double longitude,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MetroStationSummaryResponse>> GetAllAsync(
        CancellationToken cancellationToken = default);
}
