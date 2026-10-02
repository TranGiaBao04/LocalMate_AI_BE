namespace LocalMateAI.Application.DTOs.Trips;

public enum DeleteItineraryItemResultStatus
{
    Success,
    InvalidId,
    UserNotFound,
    ItemNotFound,
    TripFinalized,
    LastItem
}

public sealed record DeleteItineraryItemResult(
    DeleteItineraryItemResultStatus Status,
    IReadOnlyList<ItineraryTimelineItemResponse>? RemainingItems = null)
{
    public static DeleteItineraryItemResult Succeeded(IReadOnlyList<ItineraryTimelineItemResponse> remainingItems) =>
        new(DeleteItineraryItemResultStatus.Success, remainingItems);

    public static DeleteItineraryItemResult InvalidIds() =>
        new(DeleteItineraryItemResultStatus.InvalidId);

    public static DeleteItineraryItemResult MissingUser() =>
        new(DeleteItineraryItemResultStatus.UserNotFound);

    public static DeleteItineraryItemResult MissingItem() =>
        new(DeleteItineraryItemResultStatus.ItemNotFound);

    public static DeleteItineraryItemResult AlreadyFinalized() =>
        new(DeleteItineraryItemResultStatus.TripFinalized);

    public static DeleteItineraryItemResult CannotDeleteLastItem() =>
        new(DeleteItineraryItemResultStatus.LastItem);
}
