namespace LocalMateAI.Application.DTOs.Trips;

public enum FinalizeTripResultStatus
{
    Success,
    InvalidTripId,
    TripNotFound,
    AlreadyFinalized
}

public sealed record FinalizeTripResult(
    FinalizeTripResultStatus Status,
    FinalizeTripResponse? Response = null)
{
    public static FinalizeTripResult Succeeded(FinalizeTripResponse response) =>
        new(FinalizeTripResultStatus.Success, response);

    public static FinalizeTripResult InvalidTrip() =>
        new(FinalizeTripResultStatus.InvalidTripId);

    public static FinalizeTripResult MissingTrip() =>
        new(FinalizeTripResultStatus.TripNotFound);

    public static FinalizeTripResult AlreadyFinalized() =>
        new(FinalizeTripResultStatus.AlreadyFinalized);
}