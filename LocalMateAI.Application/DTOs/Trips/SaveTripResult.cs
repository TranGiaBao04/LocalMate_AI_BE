namespace LocalMateAI.Application.DTOs.Trips;

public enum SaveTripResultStatus
{
    Success,
    InvalidTripId,
    UserNotFound,
    TripNotFound
}

public sealed record SaveTripResult(
    SaveTripResultStatus Status,
    SaveTripResponse? Response = null)
{
    public static SaveTripResult Succeeded(SaveTripResponse response) =>
        new(SaveTripResultStatus.Success, response);

    public static SaveTripResult InvalidTrip() =>
        new(SaveTripResultStatus.InvalidTripId);

    public static SaveTripResult MissingUser() =>
        new(SaveTripResultStatus.UserNotFound);

    public static SaveTripResult MissingTrip() =>
        new(SaveTripResultStatus.TripNotFound);
}
