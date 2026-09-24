namespace LocalMateAI.Application.DTOs.Trips;

public enum DeleteTripResultStatus
{
    Success,
    InvalidTripId,
    UserNotFound,
    TripNotFound
}

public sealed record DeleteTripResult(DeleteTripResultStatus Status)
{
    public static DeleteTripResult Succeeded() =>
        new(DeleteTripResultStatus.Success);

    public static DeleteTripResult InvalidTrip() =>
        new(DeleteTripResultStatus.InvalidTripId);

    public static DeleteTripResult MissingUser() =>
        new(DeleteTripResultStatus.UserNotFound);

    public static DeleteTripResult MissingTrip() =>
        new(DeleteTripResultStatus.TripNotFound);
}
