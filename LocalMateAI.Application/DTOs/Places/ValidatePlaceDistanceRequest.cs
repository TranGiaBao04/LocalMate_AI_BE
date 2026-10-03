namespace LocalMateAI.Application.DTOs.Places;

public sealed record ValidatePlaceDistanceRequest(
    double Latitude,
    double Longitude,
    Guid? StationId = null);
