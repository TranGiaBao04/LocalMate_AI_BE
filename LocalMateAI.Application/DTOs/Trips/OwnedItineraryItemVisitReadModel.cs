using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Trips;

public sealed record OwnedItineraryItemVisitReadModel(
    Guid Id,
    Guid TripId,
    TripStatus TripStatus,
    bool IsVisited,
    DateTimeOffset? VisitedAt,
    DateTime UpdatedAt);
