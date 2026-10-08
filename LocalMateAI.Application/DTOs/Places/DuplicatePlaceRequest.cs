namespace LocalMateAI.Application.DTOs.Places;

public sealed record DetectDuplicatePlaceRequest(
    string Name,
    double Latitude,
    double Longitude);

public sealed record DuplicatePlaceCandidate(
    Guid Id,
    string Name,
    string Address,
    double Latitude,
    double Longitude,
    double DistanceMeters);

public sealed record DuplicatePlaceDetectionResult(
    IReadOnlyList<DuplicatePlaceCandidate> Candidates);
