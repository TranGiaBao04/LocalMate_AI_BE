using LocalMateAI.Application.DTOs.Geo;

namespace LocalMateAI.Application.DTOs.Trips;

public sealed record TripFeasibilityResponse(
    bool IsFeasible,
    string? Reason,
    NearestStationResult NearestStation,
    string DurationCategory,
    string BudgetTier,
    int EstimatedStopCount,
    int CandidatePlaceCount);
