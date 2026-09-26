namespace LocalMateAI.Application.DTOs.MasterData;

public sealed record MasterDataResponse(
    IReadOnlyList<MetroStationSummaryResponse> MetroStations,
    IReadOnlyList<string> PlaceCategories,
    IReadOnlyList<ReviewQuickTagResponse> ReviewQuickTags,
    IReadOnlyList<string> TripStatuses,
    IReadOnlyList<string> TravelModes,
    TripLimitsResponse TripLimits,
    IReadOnlyList<TimeSlotResponse> TimeSlots);
