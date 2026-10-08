using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;

namespace LocalMateAI.Application.Services;

public sealed class ItineraryTimelineRecalculator : IItineraryTimelineRecalculator
{
    public IReadOnlyList<TimelineItemUpdate> Recalculate(
        TimelineRecalculationInput input,
        TripPlanningSettings settings,
        MetroDayTimetable? metroTimetable = null)
    {
        var ordered = Ordered(input);

        // Giữ nguyên thứ tự và thời lượng, xếp lại giờ theo toạ độ và phương tiện của trip (chặng đầu lấy mốc bắt đầu chuyến).
        // Trip Metro: đoạn giữa hai chặng khác ga và xa nhau vẫn tính đi bộ về ga + chờ + ngồi tàu như lúc tạo lịch.
        var slots = ItineraryScheduler.Reschedule(
            ToScheduleInputs(ordered),
            input.TripStartTime,
            input.TravelMode,
            settings: settings,
            metroTimetable: metroTimetable);

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

    public int EndMinuteOfDay(
        TimelineRecalculationInput input,
        TripPlanningSettings settings,
        MetroDayTimetable? metroTimetable = null) =>
        input.TripStartTime.Hour * 60 + input.TripStartTime.Minute
        + ItineraryScheduler.TotalMinutes(
            ToScheduleInputs(Ordered(input)),
            input.TravelMode,
            origin: null,
            settings,
            input.TripStartTime,
            metroTimetable);

    private static List<TimelineItemSnapshot> Ordered(TimelineRecalculationInput input) =>
        input.RemainingItems
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.ItemId)
            .ToList();

    private static List<ScheduleInput> ToScheduleInputs(IReadOnlyList<TimelineItemSnapshot> ordered) =>
        ordered
            .Select(item => new ScheduleInput(
                item.Latitude, item.Longitude, item.EstimatedDurationMinutes,
                StationOrder: item.StationOrder, DistanceFromStationMeters: item.DistanceFromStationMeters))
            .ToList();
}
