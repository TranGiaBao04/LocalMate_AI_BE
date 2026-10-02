namespace LocalMateAI.Application.DTOs.PlaceReviews;

public enum CreatePlaceReviewResultStatus
{
    Success,
    ValidationFailed,
    InvalidItemId,
    UserNotFound,
    ItemNotFound,
    ItemNotVisited,
    AlreadyExists
}

public sealed record CreatePlaceReviewResult(
    CreatePlaceReviewResultStatus Status,
    PlaceReviewResponse? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    public static CreatePlaceReviewResult Succeeded(PlaceReviewResponse response) =>
        new(CreatePlaceReviewResultStatus.Success, response);

    public static CreatePlaceReviewResult Invalid(IReadOnlyDictionary<string, string[]> validationErrors) =>
        new(CreatePlaceReviewResultStatus.ValidationFailed, ValidationErrors: validationErrors);

    public static CreatePlaceReviewResult InvalidItem() =>
        new(CreatePlaceReviewResultStatus.InvalidItemId);

    public static CreatePlaceReviewResult MissingUser() =>
        new(CreatePlaceReviewResultStatus.UserNotFound);

    public static CreatePlaceReviewResult MissingItem() =>
        new(CreatePlaceReviewResultStatus.ItemNotFound);

    public static CreatePlaceReviewResult NotVisited() =>
        new(CreatePlaceReviewResultStatus.ItemNotVisited);

    public static CreatePlaceReviewResult Duplicate() =>
        new(CreatePlaceReviewResultStatus.AlreadyExists);
}
