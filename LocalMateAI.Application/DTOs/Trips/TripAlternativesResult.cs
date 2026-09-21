namespace LocalMateAI.Application.DTOs.Trips;

public enum TripAlternativesResultStatus
{
    Success,
    InvalidId,
    InvalidLimit,
    UserNotFound,
    ItemNotFound,
    TripFinalized
}

public sealed record TripAlternativesResult(
    TripAlternativesResultStatus Status,
    IReadOnlyList<AlternativePlaceResponse>? Alternatives = null)
{
    public static TripAlternativesResult Succeeded(IReadOnlyList<AlternativePlaceResponse> alternatives) =>
        new(TripAlternativesResultStatus.Success, alternatives);

    public static TripAlternativesResult InvalidIds() =>
        new(TripAlternativesResultStatus.InvalidId);

    public static TripAlternativesResult InvalidLimit() =>
        new(TripAlternativesResultStatus.InvalidLimit);

    public static TripAlternativesResult MissingUser() =>
        new(TripAlternativesResultStatus.UserNotFound);

    public static TripAlternativesResult MissingItem() =>
        new(TripAlternativesResultStatus.ItemNotFound);

    public static TripAlternativesResult AlreadyFinalized() =>
        new(TripAlternativesResultStatus.TripFinalized);
}
