namespace LocalMateAI.Application.DTOs.Trips;

public sealed record TripDetailResponse(
    Guid Id,
    string Status,
    string TravelMode,
    double StartLatitude,
    double StartLongitude,
    string? StationName,
    int DurationHours,
    decimal BudgetMin,
    decimal BudgetMax,
    decimal EstimatedBudget,
    int TotalDurationMinutes, // giữ nguyên nghĩa cũ: chỉ tổng thời gian tham quan
    int TotalVisitMinutes,
    int TotalTravelMinutes,
    int TotalMinutes, // tham quan + di chuyển
    TimeOnly? EndTime,
    IReadOnlyList<Guid> TagIds,
    IReadOnlyList<TripItemResponse> Items,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    DateTime? FinalizedAt,
    DateOnly? PlannedDate = null, // null với trip cũ chưa đặt ngày
    TimeOnly? StartTime = null, // giờ RỜI điểm xuất phát
    int? TravelMinutesFromOrigin = null, // thời gian đi từ điểm xuất phát tới chặng đầu
    StationRefDto? StartStation = null, // ga người dùng chọn xuất phát; null = xuất phát từ toạ độ
    StationRefDto? DestinationStation = null, // ga người dùng chọn để chơi quanh đó; null = "gần tôi"
    string? Note = null, // ghi chú người dùng nhập khi tạo lịch; null = không ghi chú
    bool NoteApplied = false); // true khi ghi chú đã được dùng để ưu tiên địa điểm
