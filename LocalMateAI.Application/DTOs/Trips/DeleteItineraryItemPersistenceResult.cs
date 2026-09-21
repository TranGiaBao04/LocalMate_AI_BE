namespace LocalMateAI.Application.DTOs.Trips;

public enum DeleteItineraryItemPersistenceStatus
{
    Deleted,
    NotFound,
    TripFinalized,
    LastItem
}

public sealed record DeleteItineraryItemPersistenceResult(
    DeleteItineraryItemPersistenceStatus Status,
    IReadOnlyList<ItineraryTimelineItemResponse>? RemainingItems = null);
