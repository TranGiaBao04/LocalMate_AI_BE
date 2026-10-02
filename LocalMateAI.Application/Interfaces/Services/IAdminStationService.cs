using LocalMateAI.Application.DTOs.Stations;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IAdminStationService
{
    Task<AdminStationsResponse> GetStationsAsync(CancellationToken cancellationToken = default);
}
