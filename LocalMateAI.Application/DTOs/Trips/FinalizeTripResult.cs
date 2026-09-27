namespace LocalMateAI.Application.DTOs.Trips;

public enum FinalizeTripResultStatus
{
    Success,
    InvalidTripId,
    TripNotFound,
    AlreadyFinalized,
    SavedTripQuotaExceeded
}

public sealed record FinalizeTripResult(
    FinalizeTripResultStatus Status,
    FinalizeTripResponse? Response = null,
    int? Used = null,
    int? Limit = null)
{
    public static FinalizeTripResult Succeeded(FinalizeTripResponse response) =>
        new(FinalizeTripResultStatus.Success, response);

    public static FinalizeTripResult InvalidTrip() =>
        new(FinalizeTripResultStatus.InvalidTripId);

    public static FinalizeTripResult MissingTrip() =>
        new(FinalizeTripResultStatus.TripNotFound);

    public static FinalizeTripResult AlreadyFinalized() =>
        new(FinalizeTripResultStatus.AlreadyFinalized);

    public static FinalizeTripResult QuotaExceeded(int used, int limit) =>
        new(FinalizeTripResultStatus.SavedTripQuotaExceeded, Used: used, Limit: limit);
}
