using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface ITripRepository
{
    Task<int> CountFinalizedByUserAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MyTripReadModel>> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Trip?> GetByIdAsync(
        Guid tripId,
        CancellationToken cancellationToken = default);

    Task<bool> AttachUserIfUnownedAsync(
        Guid tripId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> FinalizeTripAsync(
        Guid tripId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<OwnedItineraryItemVisitReadModel?> GetOwnedItineraryItemVisitAsync(
        Guid itemId,
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<bool> MarkItineraryItemVisitedIfEligibleAsync(
        Guid itemId,
        Guid userId,
        DateTimeOffset visitedAt,
        CancellationToken cancellationToken = default);

    Task<Trip?> ForkTripAsync(
        Guid sourceTripId,
        Guid userId,
        CancellationToken cancellationToken = default);

    // Trả null nếu trip không tồn tại, không thuộc userId hoặc đã bị xoá mềm.
    Task<TripDetailReadModel?> GetOwnedDetailAsync(
        Guid tripId,
        Guid userId,
        CancellationToken cancellationToken = default);

    // Lưu một Trip mới (kèm Items/Tags). BE-41 generate cũng sẽ dùng lại.
    Task AddAsync(
        Trip trip,
        CancellationToken cancellationToken = default);

    // Xoá mềm (đặt DeletedAt). Trả false nếu không có trip nào thoả điều kiện.
    Task<bool> SoftDeleteAsync(
        Guid tripId,
        Guid userId,
        CancellationToken cancellationToken = default);
}
