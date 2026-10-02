namespace LocalMateAI.Application.DTOs.Feedback;

public enum CreateFeedbackResultStatus
{
    Success,
    ValidationFailed,
    NonPersistedUser,
    TripNotFound,
    TripNotFinalized,
    AlreadyExists
}

public sealed record CreateFeedbackResult(
    CreateFeedbackResultStatus Status,
    FeedbackResponse? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    public static CreateFeedbackResult Succeeded(FeedbackResponse response) =>
        new(CreateFeedbackResultStatus.Success, response);

    public static CreateFeedbackResult ValidationFailed(
        IReadOnlyDictionary<string, string[]> validationErrors) =>
        new(CreateFeedbackResultStatus.ValidationFailed, ValidationErrors: validationErrors);

    public static CreateFeedbackResult MissingPersistedUser() =>
        new(CreateFeedbackResultStatus.NonPersistedUser);

    public static CreateFeedbackResult MissingTrip() =>
        new(CreateFeedbackResultStatus.TripNotFound);

    public static CreateFeedbackResult NotFinalized() =>
        new(CreateFeedbackResultStatus.TripNotFinalized);

    public static CreateFeedbackResult AlreadyExists() =>
        new(CreateFeedbackResultStatus.AlreadyExists);
}
