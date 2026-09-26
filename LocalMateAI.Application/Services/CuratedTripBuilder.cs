using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

/// <summary>
/// Dựng một Trip Draft từ lịch trình mẫu. Logic thuần — dễ unit test, không phụ thuộc DB/HTTP.
/// Lịch trình mẫu không lưu giờ, thời lượng từng chặng hay điểm xuất phát nên các giá trị đó được suy ra ở đây,
/// bằng cùng <see cref="ItineraryScheduler"/> với lịch do generate tạo (thứ tự vẫn giữ đúng như Admin đặt).
/// </summary>
public static class CuratedTripBuilder
{
    /// <summary>Tổng số phút của lịch mẫu, dùng để kiểm tra không qua nửa đêm trước khi dựng Trip.</summary>
    public static int TotalMinutes(
        CuratedItineraryForApplyReadModel source, TravelMode travelMode, ScheduleOrigin? origin) =>
        ItineraryScheduler.TotalMinutes(ToInputs(source), travelMode, origin);

    /// <param name="plannedStartAt">Ngày + giờ RỜI điểm xuất phát; phần giờ là mốc bắt đầu để xếp giờ.</param>
    public static Trip Build(
        Guid userId,
        CuratedItineraryForApplyReadModel source,
        double? startLatitude,
        double? startLongitude,
        DateTime plannedStartAt,
        TravelMode travelMode)
    {
        var places = source.Places.OrderBy(place => place.OrderIndex).ToList();
        if (places.Count == 0)
        {
            throw new ArgumentException("Curated itinerary must contain at least one place.", nameof(source));
        }

        // Có toạ độ user gửi thì tính đoạn đi tới chặng đầu; không thì xuất phát tại chặng đầu (đoạn đi = 0).
        var origin = startLatitude.HasValue && startLongitude.HasValue
            ? new ScheduleOrigin(startLatitude.Value, startLongitude.Value)
            : null;
        var inputs = ToInputs(source);
        var slots = ItineraryScheduler.Reschedule(inputs, TimeOnly.FromDateTime(plannedStartAt), travelMode, origin);
        var totalMinutes = ItineraryScheduler.TotalMinutes(inputs, travelMode, origin);

        var first = places[0];
        var tripId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        return new Trip
        {
            Id = tripId,
            UserId = userId,
            StartLatitude = startLatitude ?? first.Latitude,
            StartLongitude = startLongitude ?? first.Longitude,
            DurationHours = Math.Clamp((int)Math.Ceiling(totalMinutes / 60.0), 1, 24),
            BudgetMin = source.EstimatedCostMin,
            BudgetMax = source.EstimatedCostMax,
            TravelMode = travelMode,
            PlannedStartAt = plannedStartAt,
            Status = TripStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            Items = places
                .Select((place, index) => new ItineraryItem
                {
                    Id = Guid.NewGuid(),
                    TripId = tripId,
                    PlaceId = place.PlaceId,
                    OrderIndex = index,
                    ScheduledTime = slots[index].ScheduledTime,
                    EstimatedDurationMinutes = slots[index].DurationMinutes,
                    EstimatedBudget = place.EstimatedCostMax,
                    Reasoning = $"Từ lịch trình mẫu: {source.Title}",
                    CreatedAt = now,
                    UpdatedAt = now
                })
                .ToList()
        };
    }

    private static List<ScheduleInput> ToInputs(CuratedItineraryForApplyReadModel source) =>
        source.Places
            .OrderBy(place => place.OrderIndex)
            .Select(place => new ScheduleInput(
                place.Latitude,
                place.Longitude,
                ItineraryScheduler.VisitMinutesFor(place.Category.ToString()),
                place.EstimatedCostMax))
            .ToList();
}
