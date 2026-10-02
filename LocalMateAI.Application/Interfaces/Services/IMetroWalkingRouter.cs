using LocalMateAI.Application.DTOs.Maps;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IMetroWalkingRouter
{
    Task<MetroWalkingRouteResult?> FindNearestMetroStationRouteAsync(double lat, double lng, string? placeName = null, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MetroStationInfo>> GetMetroLine1StationsAsync(CancellationToken cancellationToken = default);
}
