using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Places;

/// <summary>
/// BE-93: tham số lọc / phân trang / tìm kiếm cho API danh sách địa điểm admin <c>GET /api/admin/places</c>
/// </summary>
public sealed record AdminPlaceQuery : PagedQuery
{
    public static readonly IReadOnlyCollection<string> SortFields = new[]
    {
        "name",
        "address",
        "category",
        "status",
        "isVerified",
        "estimatedCostMin",
        "estimatedCostMax",
        "createdAt",
        "updatedAt"
    };

    public PlaceCategory? Category { get; init; }
    public PlaceStatus? Status { get; init; }
    public bool? IsVerified { get; init; }
    public Guid? StationId { get; init; }
}
