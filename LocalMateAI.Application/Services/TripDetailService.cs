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

        var items = trip.Items
            .Select(item => new TripItemResponse(
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
                item.VisitedAt))
            .ToList();

        var totals = TripTotalsCalculator.Calculate(items
            .Select(item => new TripStopSnapshot(item.ScheduledTime, item.EstimatedDurationMinutes, item.EstimatedBudget))
            .ToList());

        return GetTripDetailResult.Succeeded(new TripDetailResponse(
            trip.Id,
            trip.Status.ToString(),
            trip.StartLatitude,
            trip.StartLongitude,
            trip.StationName,
            trip.DurationHours,
            trip.BudgetMin,
            trip.BudgetMax,
            totals.TotalBudget,
            totals.TotalVisitMinutes, // TotalDurationMinutes: giữ nghĩa cũ, chưa gồm di chuyển
            totals.TotalVisitMinutes,
            totals.TotalTravelMinutes,
            totals.TotalMinutes,
            totals.EndTime,
            trip.TagIds,
            items,
            trip.CreatedAt,
            trip.UpdatedAt,
            trip.FinalizedAt));
    }
}
