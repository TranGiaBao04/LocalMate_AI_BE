using LocalMateAI.Application.DTOs.Geo;
using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.Services;

namespace LocalMateAI.Application.DTOs.Trips;

/// <summary>Kết quả xác định điểm xuất phát của một yêu cầu tạo lịch.</summary>
/// <param name="StartLatitude">Toạ độ xuất phát thực dùng: toạ độ người dùng gửi, hoặc toạ độ ga nếu xuất phát từ ga.</param>
/// <param name="StartLongitude">Xem StartLatitude.</param>
/// <param name="NearestStation">Ga lên: ga gần điểm xuất phát nhất, hoặc chính ga người dùng chọn (DistanceMeters = 0).</param>
/// <param name="AnchorStation">Ga cột mốc để lấy và xếp hạng ứng viên: ga muốn chơi nếu có chọn, không thì là ga lên.</param>
/// <param name="StartStationId">Chỉ có khi người dùng chọn xuất phát từ ga.</param>
/// <param name="DestinationStationId">Chỉ có khi người dùng chọn ga muốn chơi.</param>
/// <param name="ServiceAreaFailure">Null = trong vùng phục vụ. Khác null: TripInsufficiencyReasons.OutOfServiceArea
/// (toạ độ ngoài TP.HCM) hoặc TooFarFromStationForMetro (đi Metro mà cách ga lên quá xa).</param>
/// <param name="MetroBoarding">Thông tin lên tàu và lịch tàu của ngày đi; chỉ có khi phương tiện là Metro.</param>
public sealed record TripOriginResolution(
    double StartLatitude,
    double StartLongitude,
    NearestStationResult NearestStation,
    MetroStationSummaryResponse AnchorStation,
    Guid? StartStationId,
    Guid? DestinationStationId,
    string? ServiceAreaFailure,
    MetroBoarding? MetroBoarding = null)
{
    public bool IsWithinServiceArea => ServiceAreaFailure is null;

    /// <summary>Điểm xuất phát cho bộ xếp giờ, kèm thông tin lên tàu nếu đi Metro.</summary>
    public ScheduleOrigin ToScheduleOrigin() => new(StartLatitude, StartLongitude, MetroBoarding);
}
