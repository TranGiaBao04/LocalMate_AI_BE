namespace LocalMateAI.Application.DTOs.Trips;

public sealed record VisitItineraryItemResponse(
    Guid Id,
    Guid TripId,
    bool IsVisited,
    DateTimeOffset? VisitedAt);
