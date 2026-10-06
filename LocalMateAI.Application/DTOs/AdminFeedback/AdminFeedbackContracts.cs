using System.Text.Json.Serialization;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.AdminFeedback;

// Bộ lọc chung của hai danh sách admin: đánh giá địa điểm và phản hồi chuyến đi.
public abstract record AdminFeedbackListQuery : PagedQuery
{
    public const int MinYear = 2000;
    public const int MaxYear = 2100;

    public Guid? UserId { get; init; }

    /// <summary>Ngày gửi từ (yyyy-MM-dd, giờ Việt Nam, tính cả ngày này).</summary>
    public string? From { get; init; }

    /// <summary>Ngày gửi đến (yyyy-MM-dd, giờ Việt Nam, tính cả ngày này).</summary>
    public string? To { get; init; }

    /// <summary>true ⇒ chỉ dòng có nhận xét; false ⇒ chỉ dòng không có; bỏ trống ⇒ tất cả.</summary>
    public bool? HasComment { get; init; }
}

public sealed record AdminReviewQuery : AdminFeedbackListQuery
{
    public static readonly IReadOnlyCollection<string> SortFields = ["createdAt", "rating"];

    public Guid? PlaceId { get; init; }

    public int? Rating { get; init; }
}

public sealed record AdminTripFeedbackQuery : AdminFeedbackListQuery
{
    public static readonly IReadOnlyCollection<string> SortFields = ["createdAt"];

    public FeedbackQuickTag? QuickTag { get; init; }
}

// Khoảng thời gian gửi đã đổi sang UTC, nửa mở [FromUtc, ToUtcExclusive); null ⇒ không chặn đầu đó.
public sealed record AdminFeedbackDateBounds(DateTime? FromUtc, DateTime? ToUtcExclusive);

public sealed record AdminFeedbackAuthorResponse(Guid UserId, string FullName, string Email);

// IsVisible = địa điểm đang Active và chưa xoá mềm (user còn nhìn thấy).
public sealed record AdminReviewPlaceResponse(Guid Id, string Name, bool IsVisible);

public sealed record AdminReviewResponse(
    Guid Id,
    int Rating,
    IReadOnlyList<string> QuickTags,
    string? Comment,
    DateTime CreatedAt,
    AdminFeedbackAuthorResponse User,
    AdminReviewPlaceResponse Place,
    Guid TripId,
    bool TripDeleted);

public sealed record AdminTripFeedbackResponse(
    Guid Id,
    Guid TripId,
    bool TripDeleted,
    [property: JsonConverter(typeof(JsonStringEnumConverter<FeedbackQuickTag>))]
    FeedbackQuickTag QuickTag,
    string? Comment,
    DateTime CreatedAt,
    AdminFeedbackAuthorResponse User);
