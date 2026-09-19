namespace LocalMateAI.Application.DTOs.Places;

public enum AdminPlaceOperationResultStatus
{
    Success,
    ValidationFailed,
    NotFound
}

public sealed record AdminPlaceOperationResult(
    AdminPlaceOperationResultStatus Status,
    AdminPlaceResponse? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    public static AdminPlaceOperationResult Succeeded(AdminPlaceResponse response) =>
        new(AdminPlaceOperationResultStatus.Success, response);

    public static AdminPlaceOperationResult ValidationFailed(
        IReadOnlyDictionary<string, string[]> validationErrors) =>
        new(AdminPlaceOperationResultStatus.ValidationFailed, ValidationErrors: validationErrors);

    public static AdminPlaceOperationResult Missing() =>
        new(AdminPlaceOperationResultStatus.NotFound);
}
