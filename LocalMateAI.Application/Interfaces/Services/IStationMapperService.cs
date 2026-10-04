using LocalMateAI.Application.DTOs.Places;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IStationMapperService
{
    Task<StationMappingResult> MapStationsAsync(string? rawStations, CancellationToken cancellationToken = default);
}
