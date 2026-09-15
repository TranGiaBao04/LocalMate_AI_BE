using LocalMateAI.Application.DTOs.Maps;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IRouteEstimateService
{
    RouteEstimateResult EstimateRoute(
        double originLat, double originLng,
        double destLat, double destLng,
        string? originName = null, string? destName = null);
}
