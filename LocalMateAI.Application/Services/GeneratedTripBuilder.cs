using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

/// <summary>Dựng một Trip Draft từ danh sách chặng đã xếp giờ (BE-41). Logic thuần, không phụ thuộc DB/HTTP.</summary>
public static class GeneratedTripBuilder
{
    public static Trip Build(
        Guid userId,
        TripRequestDto request,
        IReadOnlyList<FallbackStopDto> stops,
        IReadOnlyList<Guid> tagIds)
    {
        if (stops.Count == 0)
        {
            throw new ArgumentException("A generated trip must contain at least one stop.", nameof(stops));
        }

        var tripId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        return new Trip
        {
            Id = tripId,
            UserId = userId,
            StartLatitude = request.StartLatitude,
            StartLongitude = request.StartLongitude,
            DurationHours = request.DurationHours,
            BudgetMin = request.BudgetMin,
            BudgetMax = request.BudgetMax,
            TravelMode = request.TravelMode,
            Status = TripStatus.Draft,
            CreatedAt = now,
            UpdatedAt = now,
            Items = stops
                .Select(stop => new ItineraryItem
                {
                    Id = Guid.NewGuid(),
                    TripId = tripId,
                    PlaceId = stop.PlaceId,
                    OrderIndex = stop.OrderIndex,
                    ScheduledTime = stop.ScheduledTime,
                    EstimatedDurationMinutes = stop.EstimatedDurationMinutes,
                    EstimatedBudget = stop.EstimatedBudget,
                    Reasoning = stop.Reasoning,
                    CreatedAt = now,
                    UpdatedAt = now
                })
                .ToList(),
            Tags = tagIds
                .Select(tagId => new TripTag { TripId = tripId, TagId = tagId })
                .ToList()
        };
    }
}
