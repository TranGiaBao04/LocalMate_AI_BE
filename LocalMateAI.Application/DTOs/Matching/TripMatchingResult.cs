using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.DTOs.Matching;

/// <param name="InsufficiencyReason">Một giá trị của <see cref="TripInsufficiencyReasons"/>, hoặc null khi đủ.</param>
/// <param name="StationName">Tên ga lên (ga gần điểm xuất phát nhất, hoặc ga người dùng chọn xuất phát).</param>
/// <param name="AnchorStation">Ga cột mốc: ứng viên được lấy và xếp hạng quanh ga này.</param>
public sealed record TripMatchingResponse(
    bool IsSufficient,
    string? InsufficiencyReason,
    string? StationName,
    int EstimatedStopCount,
    string BudgetTier,
    IReadOnlyList<ScoredPlaceDto> Candidates,
    IReadOnlyList<ExcludedCandidateDto> Excluded,
    StationRefDto AnchorStation);

/// <param name="Origin">Điểm xuất phát đã xác định; chỉ dùng nội bộ (không nằm trong response trả cho client).</param>
public sealed record TripMatchingResult(
    TripMatchingResultStatus Status,
    TripMatchingResponse? Response = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null,
    TripOriginResolution? Origin = null);

public enum TripMatchingResultStatus
{
    Success,
    ValidationFailed
}