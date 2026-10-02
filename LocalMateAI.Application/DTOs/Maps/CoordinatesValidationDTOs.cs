namespace LocalMateAI.Application.DTOs.Maps;

public record PlaceValidationReport(
    string Id,
    string Name,
    double? Latitude,
    double? Longitude,
    bool IsValid,
    string StatusReason
);

public record DatabaseValidationSummary(
    int TotalPlacesScanned,
    int ValidCount,
    int InvalidCount,
    double PassRatePercentage,
    IReadOnlyList<PlaceValidationReport> InvalidPlaces
);
