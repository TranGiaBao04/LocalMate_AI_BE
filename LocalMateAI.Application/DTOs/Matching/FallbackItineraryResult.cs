using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.DTOs.Matching;

/// <param name="Origin">Điểm xuất phát đã xác định; chỉ dùng nội bộ (không nằm trong response trả cho client).</param>
public sealed record FallbackItineraryResult(
    FallbackItineraryStatus Status,
    FallbackItineraryPayload? Payload = null,
    IReadOnlyDictionary<string, string[]>? ValidationErrors = null,
    TripOriginResolution? Origin = null);

/// <param name="InsufficiencyReason">Một giá trị của <see cref="TripInsufficiencyReasons"/>, hoặc null khi đủ.</param>
/// <param name="StationName">Tên ga lên.</param>
/// <param name="AnchorStation">Ga cột mốc: ứng viên được lấy và xếp hạng quanh ga này.</param>
/// <param name="NoteApplied">true khi ghi chú của người dùng đã được dùng để xếp hạng địa điểm.</param>
public sealed record FallbackItineraryPayload(
    string FallbackReason, // "llm_timeout" | "llm_error" | "heuristic"
    bool IsSufficient,
    string? InsufficiencyReason,
    string StationName,
    int EstimatedStopCount,
    string BudgetTier,
    IReadOnlyList<FallbackStopDto> Stops,
    StationRefDto AnchorStation,
    bool NoteApplied = false);

public enum FallbackItineraryStatus
{
    Success,
    ValidationFailed
}