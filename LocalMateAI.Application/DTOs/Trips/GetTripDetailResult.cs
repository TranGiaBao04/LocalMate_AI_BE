namespace LocalMateAI.Application.DTOs.Trips;

public enum GetTripDetailResultStatus
{
    Success,
    InvalidTripId,
    UserNotFound,
    TripNotFound
}

public sealed record GetTripDetailResult(
    GetTripDetailResultStatus Status,
    TripDetailResponse? Response = null)
{
    public static GetTripDetailResult Succeeded(TripDetailResponse response) =>
        new(GetTripDetailResultStatus.Success, response);

    public static GetTripDetailResult InvalidTrip() =>
        new(GetTripDetailResultStatus.InvalidTripId);

    public static GetTripDetailResult MissingUser() =>
        new(GetTripDetailResultStatus.UserNotFound);

    public static GetTripDetailResult MissingTrip() =>
        new(GetTripDetailResultStatus.TripNotFound);
}
