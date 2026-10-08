using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Users;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IAdminUserRepository
{
    /// <summary>Gói hiện tại tính tại thời điểm <paramref name="nowUtc"/>.</summary>
    Task<PagedResult<AdminUserListItemResponse>> GetPagedAsync(
        AdminUserQuery query,
        DateTime nowUtc,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Mọi role + mọi gói (kể cả gói đã tắt) cho ô lọc của màn Users.</summary>
    Task<AdminUserFilterOptionsResponse> GetFilterOptionsAsync(CancellationToken cancellationToken = default);

    Task<AdminUserDetailReadModel?> GetDetailAsync(Guid userId, CancellationToken cancellationToken = default);

    Task<PagedResult<AdminUserTripResponse>> GetTripsAsync(
        Guid userId,
        AdminUserTripQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Trạng thái khoá hiện tại + người khoá; null = không có user.</summary>
    Task<AdminUserLockResponse?> GetLockStateAsync(Guid userId, CancellationToken cancellationToken = default);

    /// <summary>Chỉ đổi user đang Active (gọi trong AdminOperationExecutor).</summary>
    Task LockAsync(Guid userId, string reason, Guid lockedByUserId, DateTime lockedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>Chỉ đổi user đang Locked; xoá LockedAt/LockReason/LockedByUserId.</summary>
    Task UnlockAsync(Guid userId, CancellationToken cancellationToken = default);
}
