using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface ITripExplanationRepository
{
    /// <summary>Trip chưa xoá của đúng người dùng, kèm chặng theo thứ tự. Trả null nếu không có.</summary>
    Task<TripExplanationReadModel?> GetOwnedAsync(
        Guid tripId,
        Guid userId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Trong một transaction: đánh dấu trip đã được AI viết lý do, rồi ghi lý do cho những chặng vẫn còn
    /// đúng địa điểm đã hỏi. Trả các chặng đã ghi, hoặc null khi trip không còn là bản nháp của người dùng.
    /// </summary>
    Task<IReadOnlyList<TripExplanationUpdate>?> ApplyAsync(
        Guid tripId,
        Guid userId,
        IReadOnlyList<TripExplanationUpdate> updates,
        DateTime explainedAt,
        CancellationToken cancellationToken = default);
}
