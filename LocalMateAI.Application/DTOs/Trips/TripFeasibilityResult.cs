namespace LocalMateAI.Application.DTOs.Trips;

public enum TripFeasibilityResultStatus
{
    Success,
    ValidationFailed
}

public sealed class TripFeasibilityResult
{
    private TripFeasibilityResult(
        TripFeasibilityResultStatus status,
        TripFeasibilityResponse? response = null,
        IReadOnlyDictionary<string, string[]>? validationErrors = null)
    {
        Status = status;
        Response = response;
        ValidationErrors = validationErrors;
    }

    public TripFeasibilityResultStatus Status { get; }

    public TripFeasibilityResponse? Response { get; }

    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; }

    public static TripFeasibilityResult Succeeded(TripFeasibilityResponse response) =>
        new(TripFeasibilityResultStatus.Success, response);

    public static TripFeasibilityResult ValidationFailed(
        IReadOnlyDictionary<string, string[]> validationErrors) =>
        new(TripFeasibilityResultStatus.ValidationFailed, validationErrors: validationErrors);
}
