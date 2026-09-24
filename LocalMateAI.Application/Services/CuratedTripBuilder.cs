using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

/// <summary>
/// Dựng một Trip Draft từ lịch trình mẫu. Logic thuần — dễ unit test, không phụ thuộc DB/HTTP.
/// Lịch trình mẫu không lưu giờ, thời lượng từng chặng hay điểm xuất phát nên các giá trị đó được suy ra ở đây.
/// </summary>
public static class CuratedTripBuilder
{
    public static readonly TimeOnly DefaultStartTime = new(8, 0); // khớp FallbackItineraryBuilder.StartHour
    public const int DefaultMinutesPerStop = 90; // khớp FallbackItineraryBuilder.MinutesPerStop
    public const int MinMinutesPerStop = 30;

    public static Trip Build(
        Guid userId,
        CuratedItineraryForApplyReadModel source,
        double? startLatitude,
        double? startLongitude,
        TimeOnly? startTime)
    {
        var places = source.Places.OrderBy(place => place.OrderIndex).ToList();
        if (places.Count == 0)
        {
            throw new ArgumentException("Curated itinerary must contain at least one place.", nameof(source));
        }

        var minutesPerStop = source.EstimatedDurationMinutes > 0
            ? Math.Max(MinMinutesPerStop, source.EstimatedDurationMinutes / places.Count)
            : DefaultMinutesPerStop;

        var first = places[0];
        var start = startTime ?? DefaultStartTime;
        var tripId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        return new Trip
        {
            Id = tripId,
            UserId = userId,
            StartLatitude = startLatitude ?? first.Latitude,
            StartLongitude = startLongitude ?? first.Longitude,
            DurationHours = Math.Clamp((int)Math.Ceiling(places.Count * minutesPerStop / 60.0), 1, 24),
            BudgetMin = source.EstimatedCostMin,
            BudgetMax = source.EstimatedCostMax,
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
                    ScheduledTime = start.AddMinutes(index * minutesPerStop),
                    EstimatedDurationMinutes = minutesPerStop,
                    EstimatedBudget = place.EstimatedCostMax,
                    Reasoning = $"Từ lịch trình mẫu: {source.Title}",
                    CreatedAt = now,
                    UpdatedAt = now
                })
                .ToList()
        };
    }
}
