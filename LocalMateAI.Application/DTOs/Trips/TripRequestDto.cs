using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Trips;

/// <summary>
/// Tiêu chí chuyến đi do người dùng nhập.
/// </summary>
/// <param name="StartLatitude">Vĩ độ điểm xuất phát (thường là vị trí hiện tại của người dùng).</param>
/// <param name="StartLongitude">Kinh độ điểm xuất phát.</param>
/// <param name="DurationHours">Thời lượng chuyến đi, từ 1 đến 24 giờ.</param>
/// <param name="BudgetMin">Ngân sách tối thiểu, VNĐ, tính cho cả chuyến đi (không chia theo đầu người).</param>
/// <param name="BudgetMax">Ngân sách tối đa cho MỘT NGƯỜI, VNĐ. BE không có số người; giá địa điểm là giá một người.
/// FE không nhân theo số người khi gửi, chỉ nhân khi hiển thị tổng cho nhóm.</param>
/// <param name="TagIds">Danh sách tag sở thích; có thể rỗng.</param>
/// <param name="TravelMode">Phương tiện dùng để tính thời gian di chuyển giữa các chặng.
/// Auto (mặc định): ≤ 700 m đi bộ, xa hơn xe máy.</param>
/// <param name="PlannedDate">Ngày đi (giờ Việt Nam). Bỏ trống = hôm nay. Không ở quá khứ, không quá 90 ngày.</param>
/// <param name="StartTime">Giờ RỜI điểm xuất phát (HH:mm, giờ Việt Nam). Bỏ trống = 08:00.
/// StartTime + DurationHours không được vượt 24:00 (chưa hỗ trợ lịch qua nửa đêm).</param>
public sealed record TripRequestDto(
    double StartLatitude,
    double StartLongitude,
    int DurationHours,
    decimal BudgetMin,
    decimal BudgetMax,
    IReadOnlyList<Guid> TagIds,
    TravelMode TravelMode = TravelMode.Auto,
    DateOnly? PlannedDate = null,
    TimeOnly? StartTime = null);
