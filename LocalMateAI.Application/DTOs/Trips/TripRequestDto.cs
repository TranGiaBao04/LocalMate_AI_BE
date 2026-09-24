namespace LocalMateAI.Application.DTOs.Trips;

/// <summary>
/// Tiêu chí chuyến đi do người dùng nhập.
/// </summary>
/// <param name="StartLatitude">Vĩ độ điểm xuất phát (thường là vị trí hiện tại của người dùng).</param>
/// <param name="StartLongitude">Kinh độ điểm xuất phát.</param>
/// <param name="DurationHours">Thời lượng chuyến đi, từ 1 đến 24 giờ.</param>
/// <param name="BudgetMin">Ngân sách tối thiểu, VNĐ, tính cho cả chuyến đi (không chia theo đầu người).</param>
/// <param name="BudgetMax">Ngân sách tối đa, VNĐ, tính cho cả chuyến đi (không chia theo đầu người).
/// BE chia đều cho số điểm dừng ước tính để lọc địa điểm, xem TripCriteriaNormalizationService.</param>
/// <param name="TagIds">Danh sách tag sở thích; có thể rỗng.</param>
public sealed record TripRequestDto(
    double StartLatitude,
    double StartLongitude,
    int DurationHours,
    decimal BudgetMin,
    decimal BudgetMax,
    IReadOnlyList<Guid> TagIds);
