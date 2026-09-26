using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class TripDetailService(
    IUserRepository userRepository,
    ITripRepository tripRepository) : ITripDetailService
{
    public async Task<GetTripDetailResult> GetAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken = default)
    {
        if (tripId == Guid.Empty)
        {
            return GetTripDetailResult.InvalidTrip();
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return GetTripDetailResult.MissingUser();
        }

        var trip = await tripRepository.GetOwnedDetailAsync(tripId, userId, cancellationToken);
        if (trip is null)
        {
            return GetTripDetailResult.MissingTrip();
        }

        var stops = trip.Items
            .Select(item => new TripStopSnapshot(item.ScheduledTime, item.EstimatedDurationMinutes, item.EstimatedBudget))
            .ToList();

        var items = trip.Items
            .Select((item, index) =>
            {
                int? travelMinutes = null;
                int? distanceMeters = null;
                int? walkingMinutes = null;
                int? motorbikeMinutes = null;

                if (index > 0)
                {
                    var previous = trip.Items[index - 1];
                    var roadKm = TravelTimeEstimator.RoadDistanceKm(
                        previous.Latitude, previous.Longitude, item.Latitude, item.Longitude);
                    travelMinutes = TripTotalsCalculator.TravelGapMinutes(stops[index - 1], stops[index]);
                    distanceMeters = (int)Math.Round(roadKm * 1000);
                    walkingMinutes = TravelTimeEstimator.WalkingMinutes(roadKm);
                    motorbikeMinutes = TravelTimeEstimator.MotorbikeMinutes(roadKm);
                }

                return new TripItemResponse(
                    item.ItemId,
                    item.PlaceId,
                    item.PlaceName,
                    item.Category.ToString(),
                    item.ImageUrl,
                    item.Latitude,
                    item.Longitude,
                    item.StationName,
                    item.OrderIndex,
                    item.ScheduledTime,
                    item.EstimatedDurationMinutes,
                    item.EstimatedBudget,
                    item.Reasoning,
                    item.IsVisited,
                    item.VisitedAt,
                    travelMinutes,
                    distanceMeters,
                    walkingMinutes,
                    motorbikeMinutes);
            })
            .ToList();

        var totals = TripTotalsCalculator.Calculate(stops);

        // Đoạn đi từ điểm xuất phát tới chặng đầu = giờ chặng đầu - giờ rời. Không lưu riêng nên suy ra như đoạn giữa
        // các chặng; trip cũ chưa có PlannedStartAt thì để null và không đổi các tổng.
        int? originMinutes = null;
        if (trip.PlannedStartAt is { } plannedStart && trip.Items.Count > 0)
        {
            var gap = (int)(trip.Items[0].ScheduledTime.ToTimeSpan()
                            - TimeOnly.FromDateTime(plannedStart).ToTimeSpan()).TotalMinutes;
            originMinutes = Math.Max(0, gap);
        }

        var travelMinutes = totals.TotalTravelMinutes + (originMinutes ?? 0);

        return GetTripDetailResult.Succeeded(new TripDetailResponse(
            trip.Id,
            trip.Status.ToString(),
            trip.TravelMode.ToString(),
            trip.StartLatitude,
            trip.StartLongitude,
            trip.StationName,
            trip.DurationHours,
            trip.BudgetMin,
            trip.BudgetMax,
            totals.TotalBudget,
            totals.TotalVisitMinutes, // TotalDurationMinutes: giữ nghĩa cũ, chưa gồm di chuyển
            totals.TotalVisitMinutes,
            travelMinutes, // đã gồm đoạn đi tới chặng đầu
            totals.TotalVisitMinutes + travelMinutes,
            totals.EndTime,
            trip.TagIds,
            items,
            trip.CreatedAt,
            trip.UpdatedAt,
            trip.FinalizedAt,
            trip.PlannedStartAt is { } date ? DateOnly.FromDateTime(date) : null,
            trip.PlannedStartAt is { } time ? TimeOnly.FromDateTime(time) : null,
            originMinutes));
    }
}
