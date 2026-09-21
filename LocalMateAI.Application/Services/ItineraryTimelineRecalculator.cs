using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class ItineraryTimelineRecalculator : IItineraryTimelineRecalculator
{
    public IReadOnlyList<TimelineItemUpdate> Recalculate(IReadOnlyList<TimelineItemSnapshot> remainingItems)
    {
        var updates = new List<TimelineItemUpdate>();
        var ordered = remainingItems
            .OrderBy(item => item.OrderIndex)
            .ThenBy(item => item.ItemId)
            .ToList();

        var previousEnd = TimeSpan.Zero;
        for (var index = 0; index < ordered.Count; index++)
        {
            var item = ordered[index];
            var currentTime = item.ScheduledTime.ToTimeSpan();

            // Item đầu giữ nguyên giờ; các item sau dồn sát item trước, không bao giờ bị đẩy muộn hơn giờ cũ.
            var newTime = index == 0 || currentTime < previousEnd ? currentTime : previousEnd;

            if (item.OrderIndex != index || newTime != currentTime)
            {
                updates.Add(new TimelineItemUpdate(
                    item.ItemId,
                    index,
                    TimeOnly.FromTimeSpan(newTime)));
            }

            previousEnd = newTime + TimeSpan.FromMinutes(item.EstimatedDurationMinutes);
        }

        return updates;
    }
}
