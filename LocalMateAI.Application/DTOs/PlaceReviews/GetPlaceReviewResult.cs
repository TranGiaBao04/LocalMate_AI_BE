namespace LocalMateAI.Application.DTOs.PlaceReviews;

public enum GetPlaceReviewResultStatus
{
    Success,
    InvalidItemId,
    UserNotFound,
    ItemNotFound,
    ReviewNotFound
}

public sealed record GetPlaceReviewResult(
    GetPlaceReviewResultStatus Status,
    PlaceReviewResponse? Response = null)
{
    public static GetPlaceReviewResult Succeeded(PlaceReviewResponse response) =>
        new(GetPlaceReviewResultStatus.Success, response);

    public static GetPlaceReviewResult InvalidItem() =>
        new(GetPlaceReviewResultStatus.InvalidItemId);

    public static GetPlaceReviewResult MissingUser() =>
        new(GetPlaceReviewResultStatus.UserNotFound);

    public static GetPlaceReviewResult MissingItem() =>
        new(GetPlaceReviewResultStatus.ItemNotFound);

    public static GetPlaceReviewResult MissingReview() =>
        new(GetPlaceReviewResultStatus.ReviewNotFound);
}
