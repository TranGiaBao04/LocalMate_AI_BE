namespace LocalMateAI.Application.DTOs.Trips;

public enum VisitItineraryItemResultStatus
{
    Success,
    InvalidItemId,
    UserNotFound,
    ItemNotFound,
    TripNotFinalized
}

public sealed record VisitItineraryItemResult(
    VisitItineraryItemResultStatus Status,
    VisitItineraryItemResponse? Response = null)
{
    public static VisitItineraryItemResult Succeeded(VisitItineraryItemResponse response) =>
        new(VisitItineraryItemResultStatus.Success, response);

    public static VisitItineraryItemResult InvalidItem() =>
        new(VisitItineraryItemResultStatus.InvalidItemId);

    public static VisitItineraryItemResult MissingUser() =>
        new(VisitItineraryItemResultStatus.UserNotFound);

    public static VisitItineraryItemResult MissingItem() =>
        new(VisitItineraryItemResultStatus.ItemNotFound);

    public static VisitItineraryItemResult NotFinalized() =>
        new(VisitItineraryItemResultStatus.TripNotFinalized);
}
