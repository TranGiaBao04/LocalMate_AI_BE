using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Payments;
using LocalMateAI.Application.DTOs.Users;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IAdminUserService
{
    /// <summary><paramref name="actor"/> dùng để tính canLock/canUnlock của từng dòng.</summary>
    Task<PagedResult<AdminUserListItemResponse>> GetUsersAsync(
        AdminUserQuery query,
        AdminActor actor,
        CancellationToken cancellationToken = default);

    /// <summary>Null = không có user.</summary>
    Task<AdminUserDetailResponse?> GetUserAsync(
        Guid userId,
        AdminActor actor,
        CancellationToken cancellationToken = default);

    Task<AdminUserFilterOptionsResponse> GetFilterOptionsAsync(CancellationToken cancellationToken = default);

    /// <summary>Null = không có user.</summary>
    Task<PagedResult<AdminTransactionResponse>?> GetPaymentsAsync(
        Guid userId,
        AdminTransactionQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>Null = không có user.</summary>
    Task<PagedResult<AdminUserTripResponse>?> GetTripsAsync(
        Guid userId,
        AdminUserTripQuery query,
        CancellationToken cancellationToken = default);

    Task<AdminUserLockResult> LockAsync(Guid userId, LockUserRequest request, Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<AdminUserLockResult> UnlockAsync(Guid userId, Guid actorUserId, CancellationToken cancellationToken = default);
}
