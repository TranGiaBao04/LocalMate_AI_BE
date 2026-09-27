namespace LocalMateAI.Application.DTOs.Metro;

public enum MetroStationDeparturesResultStatus
{
    Success,
    ValidationFailed,
    StationNotFound
}

public sealed record MetroStationDeparturesResult(
    MetroStationDeparturesResultStatus Status,
    MetroStationDeparturesResponse? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    public static MetroStationDeparturesResult Succeeded(MetroStationDeparturesResponse response) =>
        new(MetroStationDeparturesResultStatus.Success, response);

    public static MetroStationDeparturesResult Invalid(IReadOnlyDictionary<string, string[]> errors) =>
        new(MetroStationDeparturesResultStatus.ValidationFailed, ValidationErrors: errors);

    public static MetroStationDeparturesResult MissingStation() =>
        new(MetroStationDeparturesResultStatus.StationNotFound);
}
