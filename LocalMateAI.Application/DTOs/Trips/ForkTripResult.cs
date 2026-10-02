namespace LocalMateAI.Application.DTOs.Trips;

public enum ForkTripResultStatus
{
    Success,
    InvalidTripId,
    TripNotFound
}

public sealed record ForkTripResult(
    ForkTripResultStatus Status,
    ForkTripResponse? Response = null)
{
    public static ForkTripResult Succeeded(ForkTripResponse response) =>
        new(ForkTripResultStatus.Success, response);

    public static ForkTripResult InvalidTrip() =>
        new(ForkTripResultStatus.InvalidTripId);

    public static ForkTripResult MissingTrip() =>
        new(ForkTripResultStatus.TripNotFound);
}