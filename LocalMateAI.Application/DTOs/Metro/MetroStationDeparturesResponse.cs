namespace LocalMateAI.Application.DTOs.Metro;

public sealed record MetroStationDeparturesResponse(
    MetroStationRefResponse Station,
    DateOnly Date,
    TimeOnly? FirstDeparture,
    TimeOnly? LastDeparture,
    bool IsEstimated,
    MetroTimetablePrecision Precision,
    bool IsWithinEffectivePeriod,
    DateOnly EffectiveFrom,
    DateOnly? EffectiveTo,
    string? Notice,
    IReadOnlyList<MetroDirectionDeparturesResponse> Directions);
