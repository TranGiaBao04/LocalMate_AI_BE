using LocalMateAI.Application.DTOs.Stations;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IAdminStationRepository
{
    /// <summary>
    /// Mọi ga + số địa điểm chưa xoá mềm gom theo (ga, category, trạng thái). Địa điểm thuộc ga gần nhất nếu cách
    /// ga đó ≤ radiusMeters, ngược lại StationId = null. Cùng cách gán với GetMetroClusterPlacesAsync (matching).
    /// </summary>
    Task<AdminStationSnapshot> GetStationPlaceCountsAsync(double radiusMeters,
        CancellationToken cancellationToken = default);
}
