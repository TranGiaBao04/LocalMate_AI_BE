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

    // Giờ kết thúc chặng cuối sau khi tính lại, bằng số phút từ 00:00. Lớn hơn 1440 nghĩa là lịch tràn qua nửa đêm
    // (giờ lưu kiểu TimeOnly sẽ quay vòng về 00:00), nên nơi gọi phải từ chối thay đổi đó.
    int EndMinuteOfDay(
        TimelineRecalculationInput input,
        TripPlanningSettings settings,
        MetroDayTimetable? metroTimetable = null);
}
