using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Payments;
using LocalMateAI.Application.DTOs.Roles;
using LocalMateAI.Application.DTOs.Users;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Application.Validators.Payments;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

/// <summary>BE-132→134: quản lý người dùng cho Admin Portal (F8).</summary>
public sealed class AdminUserService(
    IAdminUserRepository repository,
    IAdminTransactionRepository transactionRepository,
    ISubscriptionService subscriptionService,
    IAdminRoleRepository roleRepository,
    IAdminOperationExecutor operationExecutor,
    TimeProvider timeProvider) : IAdminUserService
{
    public const int MaxLockReasonLength = 500;

    public async Task<PagedResult<AdminUserListItemResponse>> GetUsersAsync(
        AdminUserQuery query,
        AdminActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var page = await repository.GetPagedAsync(query, timeProvider.GetUtcNow().UtcDateTime, cancellationToken);
        var items = page.Items
            .Select(item =>
            {
                var status = Enum.Parse<UserStatus>(item.Status);
                return item with
                {
                    CanLock = AdminUserLockRules.CanLock(item.Id, status, item.ManagesRoles, actor),
                    CanUnlock = AdminUserLockRules.CanUnlock(status, item.ManagesRoles, actor)
                };
            })
            .ToList();

        return PagedResult<AdminUserListItemResponse>.Create(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<AdminUserDetailResponse?> GetUserAsync(
        Guid userId,
        AdminActor actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var user = await repository.GetDetailAsync(userId, cancellationToken);
        if (user is null)
        {
            return null;
        }

        // Dùng chung logic /subscription/me để admin thấy đúng số liệu user thấy. Null ⇒ user vừa bị xoá giữa 2 truy vấn.
        var subscription = await subscriptionService.GetMySubscriptionAsync(userId, cancellationToken);
        if (subscription is null)
        {
            return null;
        }

        return new AdminUserDetailResponse(
            user.Id,
            user.FullName,
            user.Email,
            user.Role,
            user.ManagesRoles,
            user.Status.ToString(),
            user.LockedAt,
            user.LockReason,
            user.LockedBy,
            user.HasPassword,
            user.LoginProviders,
            subscription,
            user.Stats,
            user.CreatedAt,
            user.UpdatedAt)
        {
            CanLock = AdminUserLockRules.CanLock(user.Id, user.Status, user.ManagesRoles, actor),
            CanUnlock = AdminUserLockRules.CanUnlock(user.Status, user.ManagesRoles, actor)
        };
    }

    public Task<AdminUserFilterOptionsResponse> GetFilterOptionsAsync(CancellationToken cancellationToken = default) =>
        repository.GetFilterOptionsAsync(cancellationToken);

    // Dùng lại truy vấn giao dịch của màn Giao dịch (BE-123) để hai màn luôn khớp; chỉ thêm lọc theo user.
    public async Task<PagedResult<AdminTransactionResponse>?> GetPaymentsAsync(
        Guid userId,
        AdminTransactionQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!await repository.ExistsAsync(userId, cancellationToken))
        {
            return null;
        }

        var filter = AdminTransactionFilterQueryValidator.Normalize(query) with { UserId = userId };
        return await transactionRepository.GetTransactionsAsync(filter, query.Paging(), cancellationToken);
    }

    public async Task<PagedResult<AdminUserTripResponse>?> GetTripsAsync(
        Guid userId,
        AdminUserTripQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!await repository.ExistsAsync(userId, cancellationToken))
        {
            return null;
        }

        return await repository.GetTripsAsync(userId, query, cancellationToken);
    }

    /// <summary>
    /// BE-134: khoá tài khoản. Chạy dưới khoá AdminLocks.RoleManagement (chung với thao tác role) để luật
    /// "luôn còn ≥ 1 người quản lý role đang hoạt động" không bị 2 request đồng thời phá vỡ.
    /// </summary>
    public async Task<AdminUserLockResult> LockAsync(
        Guid userId,
        LockUserRequest request,
        Guid actorUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var reason = request.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > MaxLockReasonLength)
        {
            return AdminUserLockResult.Failed(AdminUserLockResultStatus.InvalidReason);
        }

        if (userId == actorUserId)
        {
            return AdminUserLockResult.Failed(AdminUserLockResultStatus.CannotLockSelf);
        }

        return await operationExecutor.ExecuteExclusiveAsync(
            AdminLocks.RoleManagement,
            async operationCancellationToken =>
            {
                var target = await roleRepository.GetUserRoleAsync(userId, operationCancellationToken);
                if (target is null)
                {
                    return AdminUserLockResult.Failed(AdminUserLockResultStatus.UserNotFound);
                }

                var targetManagesRoles = ManagesRoles(target);
                var actorManagesRoles = targetManagesRoles
                    && await ActorManagesRolesAsync(actorUserId, operationCancellationToken);
                if (!AdminUserLockRules.CanManage(targetManagesRoles, actorManagesRoles))
                {
                    return AdminUserLockResult.Failed(AdminUserLockResultStatus.CannotManageRoleManager);
                }

                // Đã khoá ⇒ không đổi gì (giữ thời điểm, lý do, người khoá cũ).
                if (target.Status == UserStatus.Active)
                {
                    if (targetManagesRoles
                        && await roleRepository.CountActiveRoleManagersAsync(
                            excludeUserId: userId,
                            excludeRoleId: null,
                            operationCancellationToken) == 0)
                    {
                        return AdminUserLockResult.Failed(AdminUserLockResultStatus.LastRoleManager);
                    }

                    await repository.LockAsync(
                        userId,
                        reason,
                        actorUserId,
                        timeProvider.GetUtcNow().UtcDateTime,
                        operationCancellationToken);
                }

                return AdminUserLockResult.Succeeded(await GetRequiredLockStateAsync(userId, operationCancellationToken));
            },
            cancellationToken);
    }

    /// <summary>BE-134: mở khoá. Đang Active ⇒ không đổi gì.</summary>
    public Task<AdminUserLockResult> UnlockAsync(
        Guid userId,
        Guid actorUserId,
        CancellationToken cancellationToken = default) =>
        operationExecutor.ExecuteExclusiveAsync(
            AdminLocks.RoleManagement,
            async operationCancellationToken =>
            {
                var target = await roleRepository.GetUserRoleAsync(userId, operationCancellationToken);
                if (target is null)
                {
                    return AdminUserLockResult.Failed(AdminUserLockResultStatus.UserNotFound);
                }

                var targetManagesRoles = ManagesRoles(target);
                var actorManagesRoles = targetManagesRoles
                    && await ActorManagesRolesAsync(actorUserId, operationCancellationToken);
                if (!AdminUserLockRules.CanManage(targetManagesRoles, actorManagesRoles))
                {
                    return AdminUserLockResult.Failed(AdminUserLockResultStatus.CannotManageRoleManager);
                }

                if (target.Status == UserStatus.Locked)
                {
                    await repository.UnlockAsync(userId, operationCancellationToken);
                }

                return AdminUserLockResult.Succeeded(await GetRequiredLockStateAsync(userId, operationCancellationToken));
            },
            cancellationToken);

    // Người không có ManageRoles không được khoá/mở khoá người có ManageRoles (tránh role chỉ có ManageUsers khoá hết Admin).
    private async Task<bool> ActorManagesRolesAsync(Guid actorUserId, CancellationToken cancellationToken)
    {
        var actor = await roleRepository.GetUserRoleAsync(actorUserId, cancellationToken);
        return actor is { Status: UserStatus.Active } && ManagesRoles(actor);
    }

    private static bool ManagesRoles(UserRoleReadModel user) =>
        RolePermissionRules.Resolve(user.RoleNormalizedName, user.RoleIsSystem, user.RoleStoredPermissions)
            .Contains(Permissions.ManageRoles);

    private async Task<AdminUserLockResponse> GetRequiredLockStateAsync(Guid userId, CancellationToken cancellationToken) =>
        await repository.GetLockStateAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException($"User {userId} vừa kiểm tra nhưng không đọc lại được.");
}
