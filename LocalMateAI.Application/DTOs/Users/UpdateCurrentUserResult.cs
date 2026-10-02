namespace LocalMateAI.Application.DTOs.Users;

public enum UpdateCurrentUserResultStatus
{
    Success,
    ValidationFailed,
    NoChangesRequest,
    InvalidPreference,
    UserNotFound
}

public sealed record UpdateCurrentUserResult
{
    private UpdateCurrentUserResult(
        UpdateCurrentUserResultStatus status,
        UserProfileResponse? response = null,
        IReadOnlyDictionary<string, string[]>? validationErrors = null)
    {
        Status = status;
        Response = response;
        ValidationErrors = validationErrors;
    }

    public UpdateCurrentUserResultStatus Status { get; }

    public UserProfileResponse? Response { get; }

    public IReadOnlyDictionary<string, string[]>? ValidationErrors { get; }

    public static UpdateCurrentUserResult Succeeded(UserProfileResponse response) =>
        new(UpdateCurrentUserResultStatus.Success, response);

    public static UpdateCurrentUserResult ValidationFailed(
        IReadOnlyDictionary<string, string[]> validationErrors) =>
        new(UpdateCurrentUserResultStatus.ValidationFailed, validationErrors: validationErrors);

    public static UpdateCurrentUserResult NoChanges() =>
        new(UpdateCurrentUserResultStatus.NoChangesRequest);

    public static UpdateCurrentUserResult InvalidPreference() =>
        new(UpdateCurrentUserResultStatus.InvalidPreference);

    public static UpdateCurrentUserResult UserNotFound() =>
        new(UpdateCurrentUserResultStatus.UserNotFound);
}
