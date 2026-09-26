using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IItineraryTimelineRecalculator
{
    // Trả về các item cần cập nhật (OrderIndex hoặc giờ đổi) sau khi một item bị xoá.
    IReadOnlyList<TimelineItemUpdate> Recalculate(TimelineRecalculationInput input);
}
