namespace LocalMateAI.Application.DTOs.Trips;

public enum ReplaceItineraryItemResultStatus
{
    Success,
    InvalidId,
    InvalidPlaceId,
    UserNotFound,
    ItemNotFound,
    TripFinalized,
    SamePlace,
    PlaceAlreadyInTrip,
    PlaceNotFound
}

public sealed record ReplaceItineraryItemResult(
    ReplaceItineraryItemResultStatus Status,
    ReplaceItineraryItemResponse? Response = null)
{
    public static ReplaceItineraryItemResult Succeeded(ReplaceItineraryItemResponse response) =>
        new(ReplaceItineraryItemResultStatus.Success, response);

    public static ReplaceItineraryItemResult InvalidIds() =>
        new(ReplaceItineraryItemResultStatus.InvalidId);

    public static ReplaceItineraryItemResult InvalidPlace() =>
        new(ReplaceItineraryItemResultStatus.InvalidPlaceId);

    public static ReplaceItineraryItemResult MissingUser() =>
        new(ReplaceItineraryItemResultStatus.UserNotFound);

    public static ReplaceItineraryItemResult MissingItem() =>
        new(ReplaceItineraryItemResultStatus.ItemNotFound);

    public static ReplaceItineraryItemResult AlreadyFinalized() =>
        new(ReplaceItineraryItemResultStatus.TripFinalized);

    public static ReplaceItineraryItemResult SamePlace() =>
        new(ReplaceItineraryItemResultStatus.SamePlace);

    public static ReplaceItineraryItemResult PlaceAlreadyInTrip() =>
        new(ReplaceItineraryItemResultStatus.PlaceAlreadyInTrip);

    public static ReplaceItineraryItemResult MissingPlace() =>
        new(ReplaceItineraryItemResultStatus.PlaceNotFound);
}
