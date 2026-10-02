using LocalMateAI.Application.DTOs.Maps;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IRouteEstimateService
{
    Task<RouteEstimateResult> EstimateRouteAsync(
        double originLat, double originLng,
        double destLat, double destLng,
        string? originName = null, string? destName = null,
        CancellationToken cancellationToken = default);
}
