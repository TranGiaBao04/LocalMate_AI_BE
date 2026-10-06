using LocalMateAI.Application.DTOs.PublicStats;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IPublicStatsService
{
    Task<PublicStatsResponse> GetAsync(CancellationToken cancellationToken = default);
}
