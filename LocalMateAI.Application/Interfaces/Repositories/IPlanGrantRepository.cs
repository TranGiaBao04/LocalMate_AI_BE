using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IPlanGrantRepository
{
    // null = user không tồn tại.
    Task<UserStatus?> GetUserStatusAsync(Guid userId, CancellationToken cancellationToken = default);

    Task AddAsync(PlanGrant grant, CancellationToken cancellationToken = default);

    Task<PlanGrantResponse?> GetAsync(Guid grantId, CancellationToken cancellationToken = default);

    // Mới nhất trước.
    Task<IReadOnlyList<PlanGrantResponse>> GetByUserAsync(Guid userId, CancellationToken cancellationToken = default);
}
