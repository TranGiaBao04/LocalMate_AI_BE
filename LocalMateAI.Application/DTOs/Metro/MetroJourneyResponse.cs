namespace LocalMateAI.Application.DTOs.Metro;

public sealed record MetroJourneyResponse(
    MetroStationRefResponse From,
    MetroStationRefResponse To,
    DateOnly Date,
    MetroDirection Direction,
    string TowardStationName,
    int TravelMinutes,
    int StopCount,
    bool IsEstimated,
    MetroTimetablePrecision Precision,
    bool IsWithinEffectivePeriod,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Notice,
    IReadOnlyList<MetroJourneyTripResponse> Trips,
    IReadOnlyList<MetroHeadway> Headways);
