namespace LocalMateAI.Application.DTOs.Places;

public enum AdminPlaceTagResultStatus
{
    Success,
    PlaceNotFound,
    TagNotFound
}

public sealed record AdminPlaceTagResult(
    AdminPlaceTagResultStatus Status,
    IReadOnlyList<Guid>? TagIds = null);
