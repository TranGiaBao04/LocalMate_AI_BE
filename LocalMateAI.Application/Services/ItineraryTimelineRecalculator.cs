using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class ItineraryTimelineRecalculator : IItineraryTimelineRecalculator
{
    public IReadOnlyList<TimelineItemUpdate> Recalculate(TimelineRecalculationInput input)
    {
        var ordered = input.RemainingItems
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.ItemId)
            .ToList();

        // Giữ nguyên thứ tự và thời lượng, xếp lại giờ theo toạ độ và phương tiện của trip (chặng đầu lấy mốc bắt đầu chuyến).
        var slots = ItineraryScheduler.Reschedule(
            ordered
                .Select(item => new ScheduleInput(item.Latitude, item.Longitude, item.EstimatedDurationMinutes))
                .ToList(),
            input.TripStartTime,
            input.TravelMode);

        var updates = new List<TimelineItemUpdate>();
        for (var index = 0; index < ordered.Count; index++)
        {
            var item = ordered[index];
            var scheduledTime = slots[index].ScheduledTime;

            if (item.OrderIndex != index || item.ScheduledTime != scheduledTime)
            {
                updates.Add(new TimelineItemUpdate(item.ItemId, index, scheduledTime));
            }
        }

        return updates;
    }
}
