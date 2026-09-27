namespace LocalMateAI.Application.DTOs.Trips;

public enum GenerateTripResultStatus
{
    Success,
    ValidationFailed,
    InvalidTags,
    UserNotFound,
    NoPlaces,
    QuotaExceeded
}

public sealed record GenerateTripResult(
    GenerateTripResultStatus Status,
    TripDetailResponse? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null,
    string? Reason = null, // NoPlaces: "OutOfServiceArea" | "InsufficientCandidates"
    int? Used = null,
    int? Limit = null,
    DateTime? ResetAt = null)
{
    public static GenerateTripResult Succeeded(TripDetailResponse response) =>
        new(GenerateTripResultStatus.Success, response);

    public static GenerateTripResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(GenerateTripResultStatus.ValidationFailed, ValidationErrors: errors);

    public static GenerateTripResult UnknownTags() => new(GenerateTripResultStatus.InvalidTags);

    public static GenerateTripResult MissingUser() => new(GenerateTripResultStatus.UserNotFound);

    public static GenerateTripResult NoSuitablePlaces(string reason) =>
        new(GenerateTripResultStatus.NoPlaces, Reason: reason);

    public static GenerateTripResult QuotaExceeded(int used, int limit, DateTime resetAt) =>
        new(
            GenerateTripResultStatus.QuotaExceeded,
            Used: used,
            Limit: limit,
            ResetAt: resetAt);
}
