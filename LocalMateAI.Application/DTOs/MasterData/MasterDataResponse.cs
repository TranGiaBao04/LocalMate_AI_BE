namespace LocalMateAI.Application.DTOs.MasterData;

public sealed record MasterDataResponse(
    IReadOnlyList<MetroStationSummaryResponse> MetroStations,
    IReadOnlyList<string> PlaceCategories);
