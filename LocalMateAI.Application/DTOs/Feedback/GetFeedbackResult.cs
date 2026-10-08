namespace LocalMateAI.Application.DTOs.Feedback;

public enum GetFeedbackResultStatus
{
    Success,
    NonPersistedUser,
    TripNotFound,
    FeedbackNotFound
}

public sealed record GetFeedbackResult(
    GetFeedbackResultStatus Status,
    FeedbackResponse? Response = null)
{
    public static GetFeedbackResult Succeeded(FeedbackResponse response) =>
        new(GetFeedbackResultStatus.Success, response);

    public static GetFeedbackResult MissingPersistedUser() =>
        new(GetFeedbackResultStatus.NonPersistedUser);

    public static GetFeedbackResult MissingTrip() =>
        new(GetFeedbackResultStatus.TripNotFound);

    public static GetFeedbackResult MissingFeedback() =>
        new(GetFeedbackResultStatus.FeedbackNotFound);
}
