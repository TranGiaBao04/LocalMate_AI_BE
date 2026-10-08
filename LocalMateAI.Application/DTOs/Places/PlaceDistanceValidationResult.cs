namespace LocalMateAI.Application.DTOs.Places;

public enum PlaceDistanceValidationStatus
{
    Success,
    PlaceNotFound,
    StationNotFound,
    InvalidCoordinates
}

public sealed record PlaceDistanceValidationResult(
    Guid StationId,
    string StationName,
    double StationLatitude,
    double StationLongitude,
    double DistanceMeters,
    bool IsWithinThreshold,
    bool HasWarning,
    string? WarningMessage,
    PlaceDistanceValidationStatus Status = PlaceDistanceValidationStatus.Success);
