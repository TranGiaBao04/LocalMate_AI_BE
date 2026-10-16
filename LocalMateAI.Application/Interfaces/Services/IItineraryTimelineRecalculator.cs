using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Services;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IItineraryTimelineRecalculator
{
    // Trả về các item cần cập nhật (OrderIndex hoặc giờ đổi) sau khi một item bị xoá hoặc bị thay địa điểm.
    // metroTimetable: lịch tàu của ngày đi, chỉ cần cho trip Metro (không có thì Metro được tính như Auto).
    IReadOnlyList<TimelineItemUpdate> Recalculate(
        TimelineRecalculationInput input,
        TripPlanningSettings settings,
        MetroDayTimetable? metroTimetable = null);

    // Giờ kết thúc chặng cuối sau khi tính lại, bằng số phút từ 00:00 của ngày của chặng đầu (lớn hơn 1440 nghĩa là
    // kết thúc sau nửa đêm). Nơi gọi dùng để chặn lịch dài quá 24 giờ và cảnh báo vượt số giờ rảnh.
    int EndMinuteOfDay(
        TimelineRecalculationInput input,
        TripPlanningSettings settings,
        MetroDayTimetable? metroTimetable = null);
}
