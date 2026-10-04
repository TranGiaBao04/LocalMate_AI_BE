using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class TripItemReplacementService(
    IUserRepository userRepository,
    IItineraryItemRepository itineraryItemRepository,
    IPlaceRepository placeRepository,
    IGeoService geoService,
    ISystemSettingProvider settings,
    IItineraryTimelineRecalculator timelineRecalculator,
    IMetroTimetableSource metroTimetableSource) : ITripItemReplacementService
{
    public const string DifferentStationWarning = "different_station";
    public const string HigherCostWarning = "higher_cost";

    /// <summary>Sau khi thay, lịch kết thúc muộn hơn số giờ rảnh người dùng đã nhập lúc tạo lịch.</summary>
    public const string ExceedsDurationWarning = "exceeds_duration";

    private const int MinutesPerDay = 24 * 60;

    public async Task<ReplaceItineraryItemResult> ReplaceAsync(
        Guid userId,
        Guid tripId,
        Guid itemId,
        Guid newPlaceId,
        CancellationToken cancellationToken = default)
    {
        if (tripId == Guid.Empty || itemId == Guid.Empty)
        {
            return ReplaceItineraryItemResult.InvalidIds();
        }

        if (newPlaceId == Guid.Empty)
        {
            return ReplaceItineraryItemResult.InvalidPlace();
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return ReplaceItineraryItemResult.MissingUser();
        }

        var item = await itineraryItemRepository.GetOwnedItemForAlternativesAsync(
            tripId,
            itemId,
            userId,
            cancellationToken);
        if (item is null)
        {
            return ReplaceItineraryItemResult.MissingItem();
        }

        if (item.TripStatus == TripStatus.Finalized)
        {
            return ReplaceItineraryItemResult.AlreadyFinalized();
        }

        if (newPlaceId == item.PlaceId)
        {
            return ReplaceItineraryItemResult.SamePlace();
        }

        if (item.TripPlaceIds.Contains(newPlaceId))
        {
            return ReplaceItineraryItemResult.PlaceAlreadyInTrip();
        }

        var newPlace = await placeRepository.GetActiveByIdAsync(newPlaceId, cancellationToken);
        if (newPlace is null)
        {
            return ReplaceItineraryItemResult.MissingPlace();
        }

        var warnings = await BuildWarningsAsync(item.PlaceId, newPlace, cancellationToken);

        // Đọc thông số trước khi mở transaction; lần tính lại giờ dùng chung một bộ số.
        var planning = await TripPlanningSettings.LoadAsync(settings, cancellationToken);
        int? endMinuteOfDay = null;

        var outcome = await itineraryItemRepository.ReplaceItemPlaceAndRecalculateTimelineAsync(
            tripId,
            itemId,
            userId,
            newPlaceId,
            newPlace.EstimatedCostMax,
            input =>
            {
                // Trip Metro cần lịch tàu của ngày đi; ngày chỉ biết sau khi repository đọc trip trong transaction.
                var metroTimetable = input.TravelMode == TravelMode.Metro && input.PlannedDate is { } plannedDate
                    ? MetroDayTimetable.For(metroTimetableSource.Timetable, plannedDate)
                    : null;

                // Địa điểm mới xa hơn có thể đẩy lịch qua 24:00; giờ lưu kiểu TimeOnly sẽ quay vòng nên phải từ chối.
                var end = timelineRecalculator.EndMinuteOfDay(input, planning, metroTimetable);
                if (end > MinutesPerDay)
                {
                    return null;
                }

                endMinuteOfDay = end;
                return timelineRecalculator.Recalculate(input, planning, metroTimetable);
            },
            cancellationToken);

        if (outcome.Status == ReplaceItemPersistenceStatus.NotEligible)
        {
            return await ExplainFailureAsync(tripId, itemId, userId, newPlaceId, cancellationToken);
        }

        if (outcome.Status == ReplaceItemPersistenceStatus.CrossesMidnight)
        {
            return ReplaceItineraryItemResult.WouldCrossMidnight();
        }

        // Không chặn khi lịch dài hơn số giờ rảnh ban đầu, chỉ cảnh báo để FE hiển thị.
        if (item.TripPlannedStartAt is { } leaveAt
            && endMinuteOfDay > leaveAt.Hour * 60 + leaveAt.Minute + item.TripDurationHours * 60)
        {
            warnings.Add(ExceedsDurationWarning);
        }

        var replaced = outcome.Item!;
        return ReplaceItineraryItemResult.Succeeded(new ReplaceItineraryItemResponse(
            replaced.ItemId,
            replaced.TripId,
            replaced.OrderIndex,
            replaced.ScheduledTime,
            replaced.EstimatedDurationMinutes,
            replaced.EstimatedBudget,
            ToSummary(newPlace),
            warnings,
            outcome.Items ?? []));
    }

    private async Task<List<string>> BuildWarningsAsync(
        Guid currentPlaceId,
        PlaceReadModel newPlace,
        CancellationToken cancellationToken)
    {
        var currentPlace = await placeRepository.GetByIdAsync(currentPlaceId, cancellationToken)
            ?? throw new InvalidOperationException("Current itinerary place was not found.");
        var currentStation = await geoService.FindNearestStationForPlaceAsync(currentPlaceId, cancellationToken)
            ?? throw new InvalidOperationException("No metro stations found.");
        var newStation = await geoService.FindNearestStationForPlaceAsync(newPlace.Id, cancellationToken)
            ?? throw new InvalidOperationException("No metro stations found.");

        var warnings = new List<string>();
        if (currentStation.StationId != newStation.StationId)
        {
            warnings.Add(DifferentStationWarning);
        }

        var maxCostIncreasePercent = await settings.GetIntAsync(
            SystemSettingKeys.AlternativeMaxCostIncreasePercent,
            cancellationToken);
        if (!AlternativePlaceFinder.IsWithinCostTolerance(
                currentPlace.EstimatedCostMax, newPlace.EstimatedCostMax, maxCostIncreasePercent))
        {
            warnings.Add(HigherCostWarning);
        }

        return warnings;
    }

    // Lệnh cập nhật có điều kiện không áp dụng được: đọc lại để trả đúng lý do (trip vừa chốt, địa điểm vừa được thêm...).
    private async Task<ReplaceItineraryItemResult> ExplainFailureAsync(
        Guid tripId,
        Guid itemId,
        Guid userId,
        Guid newPlaceId,
        CancellationToken cancellationToken)
    {
        var latest = await itineraryItemRepository.GetOwnedItemForAlternativesAsync(
            tripId,
            itemId,
            userId,
            cancellationToken);
        if (latest is null)
        {
            return ReplaceItineraryItemResult.MissingItem();
        }

        if (latest.TripStatus == TripStatus.Finalized)
        {
            return ReplaceItineraryItemResult.AlreadyFinalized();
        }

        return latest.TripPlaceIds.Contains(newPlaceId)
            ? ReplaceItineraryItemResult.PlaceAlreadyInTrip()
            : ReplaceItineraryItemResult.MissingItem();
    }

    private static PlaceSummaryResponse ToSummary(PlaceReadModel place) =>
        new(
            place.Id,
            place.Name,
            place.Address,
            place.Latitude,
            place.Longitude,
            place.Category,
            place.EstimatedCostMin,
            place.EstimatedCostMax,
            place.ImageUrl);
}
