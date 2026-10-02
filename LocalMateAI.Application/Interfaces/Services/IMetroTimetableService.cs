using LocalMateAI.Application.DTOs.Metro;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IMetroTimetableService
{
    Task<MetroStationDeparturesResult> GetStationDeparturesAsync(
        int stationOrder,
        DateOnly? date,
        CancellationToken cancellationToken = default);

    Task<MetroJourneyResult> GetJourneyAsync(
        int fromStationOrder,
        int toStationOrder,
        DateOnly? date,
        CancellationToken cancellationToken = default);
}
