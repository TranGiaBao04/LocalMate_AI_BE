using LocalMateAI.Application.DTOs.Matching;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IMetroClusterMatchingService
{
    /// <summary>
    /// Địa điểm Active thuộc ga xuất phát và các ga kề. Bán kính cụm ga và số ga kề đọc từ SystemSettings
    /// (Stations.ClusterRadiusMeters, Trips.AdjacentStationWindow) — nơi gọi không tự truyền.
    /// </summary>
    Task<IReadOnlyList<PlaceCandidateDto>> GetCandidatesAsync(
        Guid originStationId,
        CancellationToken cancellationToken = default);
}
