using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Itineraries;

/// <summary>Phân trang / tìm kiếm cho <c>GET /api/admin/curated-itineraries</c>. Search tìm không dấu trên tiêu đề.</summary>
public sealed record AdminCuratedItineraryQuery : PagedQuery
{
    public static readonly IReadOnlyCollection<string> SortFields = ["updatedAt", "createdAt", "title"];
}

/// <summary>Body của POST và PUT (PUT thay toàn bộ). Thứ tự <c>PlaceIds</c> là thứ tự đi.</summary>
public sealed record SaveCuratedItineraryRequest(
    string? Title,
    string? Description,
    string? CoverImageUrl,
    IReadOnlyList<Guid>? PlaceIds);

/// <param name="IsDeleted">
/// true = địa điểm đã xoá, phải bỏ khỏi lịch; false mà Status khác Active = đang ẩn, bật lại được ở màn Địa điểm.
/// </param>
public sealed record AdminCuratedItineraryItemResponse(
    Guid PlaceId,
    string Name,
    string Category,
    string Status,
    bool IsDeleted,
    int OrderIndex,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax);

/// <param name="StationName">Ga gần nhất của chặng đầu; null khi lịch chưa có chặng hoặc DB chưa có ga.</param>
/// <param name="UnavailablePlaceCount">Số chặng có địa điểm không Active (user không thấy các chặng này).</param>
public sealed record AdminCuratedItineraryResponse(
    Guid Id,
    string Title,
    string? Description,
    string? CoverImageUrl,
    int EstimatedDurationMinutes,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax,
    string? StationName,
    int UnavailablePlaceCount,
    IReadOnlyList<AdminCuratedItineraryItemResponse> Items,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>Địa điểm admin chọn cho lịch mẫu, đọc lên để kiểm tra và tính thời lượng/chi phí.</summary>
public sealed record CuratedPlaceCandidate(
    Guid Id,
    string Name,
    PlaceStatus Status,
    bool IsDeleted,
    PlaceCategory Category,
    double Latitude,
    double Longitude,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax);

/// <summary>Nội dung đã kiểm tra và tính xong, sẵn sàng ghi.</summary>
public sealed record CuratedItineraryDraft(
    string Title,
    string? Description,
    string? CoverImageUrl,
    int EstimatedDurationMinutes,
    decimal EstimatedCostMin,
    decimal EstimatedCostMax,
    IReadOnlyList<Guid> PlaceIds);

public enum AdminCuratedItineraryResultStatus
{
    Success,
    ValidationFailed,
    NotFound
}

public sealed record AdminCuratedItineraryResult(
    AdminCuratedItineraryResultStatus Status,
    AdminCuratedItineraryResponse? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null)
{
    public static AdminCuratedItineraryResult Succeeded(AdminCuratedItineraryResponse response) =>
        new(AdminCuratedItineraryResultStatus.Success, response);

    public static AdminCuratedItineraryResult Invalid(IReadOnlyDictionary<string, string[]> validationErrors) =>
        new(AdminCuratedItineraryResultStatus.ValidationFailed, ValidationErrors: validationErrors);

    public static AdminCuratedItineraryResult Missing() =>
        new(AdminCuratedItineraryResultStatus.NotFound);
}

public enum DeleteAdminCuratedItineraryResultStatus
{
    Success,
    NotFound
}
