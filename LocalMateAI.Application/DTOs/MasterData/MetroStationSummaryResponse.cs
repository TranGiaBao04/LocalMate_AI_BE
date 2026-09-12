namespace LocalMateAI.Application.DTOs.MasterData;

public sealed record MetroStationSummaryResponse(
    Guid Id,
    string Name,
    int Order,
    double Latitude,
    double Longitude);
