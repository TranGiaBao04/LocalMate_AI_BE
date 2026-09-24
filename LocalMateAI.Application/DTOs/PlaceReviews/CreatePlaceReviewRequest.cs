namespace LocalMateAI.Application.DTOs.PlaceReviews;

/// <summary>
/// Đánh giá một địa điểm trong lịch trình đã chốt.
/// </summary>
/// <param name="Rating">Số sao, từ 1 đến 5.</param>
/// <param name="QuickTags">Tối đa 3 tag nhanh, thuộc danh sách <c>ReviewQuickTags</c>; bỏ trống được.</param>
/// <param name="Comment">Nhận xét tối đa 1000 ký tự; bỏ trống được.</param>
public sealed record CreatePlaceReviewRequest(
    int Rating,
    string[]? QuickTags,
    string? Comment);
