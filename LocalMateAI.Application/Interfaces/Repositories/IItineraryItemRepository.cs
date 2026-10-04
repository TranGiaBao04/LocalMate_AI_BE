using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IItineraryItemRepository
{
    // Trả null nếu item không tồn tại, không thuộc tripId, hoặc trip không thuộc userId.
    Task<OwnedItemAlternativesReadModel?> GetOwnedItemForAlternativesAsync(
        Guid tripId,
        Guid itemId,
        Guid userId,
        CancellationToken cancellationToken = default);

    // Thay địa điểm của item rồi tính lại giờ các chặng trong cùng một transaction.
    // NotEligible: item không còn thuộc user, trip đã Finalized, hoặc newPlaceId vừa xuất hiện trong trip.
    // recalculateTimeline nhận mọi chặng (item đã mang toạ độ địa điểm mới) và trả về những chặng cần đổi giờ;
    // trả null nghĩa là từ chối (lịch tràn qua nửa đêm) ⇒ không ghi gì, kết quả CrossesMidnight.
    Task<ReplaceItemPersistenceResult> ReplaceItemPlaceAndRecalculateTimelineAsync(
        Guid tripId,
        Guid itemId,
        Guid userId,
        Guid newPlaceId,
        decimal newEstimatedBudget,
        Func<TimelineRecalculationInput, IReadOnlyList<TimelineItemUpdate>?> recalculateTimeline,
        CancellationToken cancellationToken = default);

    // Xoá item và đánh lại OrderIndex/giờ của các item còn lại trong cùng một transaction.
    // recalculateTimeline nhận các item còn lại (sau khi xoá) và trả về những item cần cập nhật.
    Task<DeleteItineraryItemPersistenceResult> DeleteItemAndRecalculateTimelineAsync(
        Guid tripId,
        Guid itemId,
        Guid userId,
        Func<TimelineRecalculationInput, IReadOnlyList<TimelineItemUpdate>> recalculateTimeline,
        CancellationToken cancellationToken = default);
}
