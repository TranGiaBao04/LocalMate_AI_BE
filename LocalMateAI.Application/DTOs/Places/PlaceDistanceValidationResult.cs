namespace LocalMateAI.Application.DTOs.Places;

public sealed record PlaceDistanceValidationResult(
    Guid StationId,
    string StationName,
    double StationLatitude,
    double StationLongitude,
    double DistanceMeters,
    bool IsWithinThreshold,
    bool HasWarning,
    string? WarningMessage);
