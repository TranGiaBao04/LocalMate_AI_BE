using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Settings;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

public sealed class TripItemDeletionService(
    IUserRepository userRepository,
    IItineraryItemRepository itineraryItemRepository,
    IItineraryTimelineRecalculator timelineRecalculator,
    ISystemSettingProvider settings,
    IMetroTimetableSource metroTimetableSource) : ITripItemDeletionService
{
    public async Task<DeleteItineraryItemResult> DeleteAsync(
        Guid userId,
        Guid tripId,
        Guid itemId,
        CancellationToken cancellationToken = default)
    {
        if (tripId == Guid.Empty || itemId == Guid.Empty)
        {
            return DeleteItineraryItemResult.InvalidIds();
        }

        var user = await userRepository.GetByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return DeleteItineraryItemResult.MissingUser();
        }

        // Đọc thông số trước khi mở transaction xoá; cả lần tính lại giờ dùng chung một bộ số.
        var planning = await TripPlanningSettings.LoadAsync(settings, cancellationToken);
        var outcome = await itineraryItemRepository.DeleteItemAndRecalculateTimelineAsync(
            tripId,
            itemId,
            userId,
            // Trip Metro cần lịch tàu của ngày đi; ngày chỉ biết sau khi repository đọc trip trong transaction.
            input => timelineRecalculator.Recalculate(
                input,
                planning,
                input.TravelMode == TravelMode.Metro && input.PlannedDate is { } plannedDate
                    ? MetroDayTimetable.For(metroTimetableSource.Timetable, plannedDate)
                    : null),
            cancellationToken);

        return outcome.Status switch
        {
            DeleteItineraryItemPersistenceStatus.Deleted =>
                DeleteItineraryItemResult.Succeeded(outcome.RemainingItems ?? []),
            DeleteItineraryItemPersistenceStatus.NotFound => DeleteItineraryItemResult.MissingItem(),
            DeleteItineraryItemPersistenceStatus.TripFinalized => DeleteItineraryItemResult.AlreadyFinalized(),
            DeleteItineraryItemPersistenceStatus.LastItem => DeleteItineraryItemResult.CannotDeleteLastItem(),
            _ => throw new InvalidOperationException("Unsupported delete itinerary item persistence status.")
        };
    }
}
