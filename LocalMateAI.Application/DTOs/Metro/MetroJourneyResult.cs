namespace LocalMateAI.Application.DTOs.Metro;

public enum MetroJourneyResultStatus
{
    Success,
    ValidationFailed,
    StationNotFound
}

public sealed record MetroJourneyResult(
    MetroJourneyResultStatus Status,
    MetroJourneyResponse? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    public static MetroJourneyResult Succeeded(MetroJourneyResponse response) =>
        new(MetroJourneyResultStatus.Success, response);

    public static MetroJourneyResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(MetroJourneyResultStatus.ValidationFailed, ValidationErrors: errors);

    public static MetroJourneyResult MissingStation() =>
        new(MetroJourneyResultStatus.StationNotFound);
}
