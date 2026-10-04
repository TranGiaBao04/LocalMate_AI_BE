using LocalMateAI.Application.DTOs.Geo;

namespace LocalMateAI.Application.DTOs.Trips;

/// <param name="Reason">Một giá trị của TripInsufficiencyReasons, hoặc null khi khả thi.</param>
/// <param name="NearestStation">Ga lên (ga gần điểm xuất phát nhất, hoặc ga người dùng chọn xuất phát).</param>
/// <param name="AnchorStation">Ga cột mốc: ứng viên được lấy và xếp hạng quanh ga này.</param>
/// <param name="SuggestedStations">Chỉ có phần tử khi Reason là InsufficientCandidates.</param>
public sealed record TripFeasibilityResponse(
    bool IsFeasible,
    string? Reason,
    NearestStationResult NearestStation,
    string DurationCategory,
    string BudgetTier,
    int EstimatedStopCount,
    int CandidatePlaceCount,
    StationRefDto AnchorStation,
    IReadOnlyList<SuggestedStationDto> SuggestedStations);
