using LocalMateAI.Domain.Entities;
namespace LocalMateAI.Application.Interfaces.Repositories;

public interface ISingleItineraryRepository
{
    Task<SingleItineraryProductVersion?> GetCurrentVersionAsync(DateTime nowUtc, CancellationToken ct = default);
    Task<SingleItineraryProductVersion?> GetVersionAsync(Guid id, CancellationToken ct = default);
    Task<PaymentOrder?> GetAttemptAsync(Guid userId, Guid attemptId, CancellationToken ct = default);
    Task<SingleItineraryEntitlement?> GetForOrderAsync(Guid orderId, CancellationToken ct = default);
    Task<SingleItineraryEntitlement?> LockOwnedAsync(Guid id, Guid userId, CancellationToken ct = default);
    Task<IReadOnlyList<SingleItineraryEntitlement>> GetOwnedAsync(Guid userId, CancellationToken ct = default);
    Task AddAsync(SingleItineraryEntitlement entitlement, CancellationToken ct = default);
    Task SaveChangesAsync(CancellationToken ct = default);
}
