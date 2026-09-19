namespace LocalMateAI.Application.DTOs.Places;

public enum AdminPlaceModerationResultStatus
{
    Success,
    InvalidStatus,
    NotFound,
    InvalidStatusTransition,
    VerificationRequiresActive
}

public sealed record AdminPlaceModerationResult(
    AdminPlaceModerationResultStatus Status,
    AdminPlaceResponse? Response = null)
{
    public static AdminPlaceModerationResult Succeeded(AdminPlaceResponse response) =>
        new(AdminPlaceModerationResultStatus.Success, response);

    public static AdminPlaceModerationResult InvalidStatus() =>
        new(AdminPlaceModerationResultStatus.InvalidStatus);

    public static AdminPlaceModerationResult Missing() =>
        new(AdminPlaceModerationResultStatus.NotFound);

    public static AdminPlaceModerationResult InvalidTransition() =>
        new(AdminPlaceModerationResultStatus.InvalidStatusTransition);

    public static AdminPlaceModerationResult RequiresActive() =>
        new(AdminPlaceModerationResultStatus.VerificationRequiresActive);
}
