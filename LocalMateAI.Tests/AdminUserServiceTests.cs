using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Payments;
using LocalMateAI.Application.DTOs.Roles;
using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.DTOs.Users;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Tests;

public sealed class AdminUserServiceTests
{
    private static readonly Guid UserId = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid ActorId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 5, 0, 0, TimeSpan.Zero);
    private static readonly AdminActor Admin = new(ActorId, ManagesRoles: true);
    private static readonly AdminActor SupportAgent = new(ActorId, ManagesRoles: false);

    private readonly FakeAdminUserRepository users = new();
    private readonly FakeTransactionRepository transactions = new();
    private readonly FakeSubscriptionService subscriptions = new();
    private readonly FakeRoleRepository roles = new();
    private readonly FakeOperationExecutor executor = new();

    private AdminUserService Service() =>
        new(users, transactions, subscriptions, roles, executor, new FixedTimeProvider(Now));

    [Fact]
    public async Task UnknownUser_EveryLookupReturnsNull_WithoutQueryingHistory()
    {
        var service = Service();

        Assert.Null(await service.GetUserAsync(UserId, Admin));
        Assert.Null(await service.GetPaymentsAsync(UserId, new AdminTransactionQuery()));
        Assert.Null(await service.GetTripsAsync(UserId, new AdminUserTripQuery()));
        Assert.Empty(transactions.Filters);
        Assert.Equal(0, users.TripQueries);
        Assert.Equal(0, subscriptions.Calls);
    }

    [Fact]
    public async Task Detail_CombinesReadModelWithSubscriptionOfSameUser()
    {
        users.Detail = Detail();
        subscriptions.Response = new SubscriptionMeResponse("TripPass", null,
            new SubscriptionUsageResponse(1, null, DateTime.UtcNow), new SubscriptionSavedTripsResponse(2, 3));

        var detail = await Service().GetUserAsync(UserId, Admin);

        Assert.NotNull(detail);
        Assert.Equal("Locked", detail.Status);
        Assert.Same(subscriptions.Response, detail.Subscription);
        Assert.Equal(UserId, subscriptions.LastUserId);
        Assert.Equal(["Google"], detail.LoginProviders);
    }

    [Fact]
    public async Task Detail_UserRemovedBeforeSubscriptionRead_ReturnsNull()
    {
        users.Detail = Detail();

        Assert.Null(await Service().GetUserAsync(UserId, Admin));
    }

    [Fact]
    public async Task Payments_AlwaysFilterByRouteUser_AndKeepTransactionFilters()
    {
        users.Exists = true;

        var page = await Service().GetPaymentsAsync(UserId, new AdminTransactionQuery
        {
            Status = " paid ",
            OperationType = "Upgrade",
            Page = 2,
            PageSize = 5,
            SortBy = "amount"
        });

        Assert.NotNull(page);
        var filter = Assert.Single(transactions.Filters);
        Assert.Equal(UserId, filter.UserId);
        Assert.Equal(PaymentOrderStatus.Paid, filter.Status);
        Assert.Equal(PaymentOrderType.Upgrade, filter.OperationType);
        Assert.Equal(2, transactions.LastPaging!.Page);
        Assert.Equal("amount", transactions.LastPaging.SortBy);
    }

    [Fact]
    public async Task Trips_ExistingUser_DelegatesToRepository()
    {
        users.Exists = true;

        Assert.NotNull(await Service().GetTripsAsync(UserId, new AdminUserTripQuery { IncludeDeleted = true }));
        Assert.Equal(1, users.TripQueries);
    }

    [Fact]
    public async Task List_FlagsDependOnViewer()
    {
        var otherAdmin = Guid.NewGuid();
        users.Items =
        [
            ListItem(otherAdmin, "Active", managesRoles: true),
            ListItem(UserId, "Locked", managesRoles: false),
            ListItem(ActorId, "Active", managesRoles: true)
        ];

        var asAdmin = (await Service().GetUsersAsync(new AdminUserQuery(), Admin)).Items;
        var asSupport = (await Service().GetUsersAsync(new AdminUserQuery(), SupportAgent)).Items;

        Assert.Equal([true, false, false], asAdmin.Select(item => item.CanLock));
        Assert.Equal([false, true, false], asAdmin.Select(item => item.CanUnlock));
        Assert.Equal([false, false, false], asSupport.Select(item => item.CanLock));
        Assert.Equal([false, true, false], asSupport.Select(item => item.CanUnlock));
        Assert.Equal(3, (await Service().GetUsersAsync(new AdminUserQuery(), Admin)).TotalCount);
    }

    [Fact]
    public async Task Detail_FlagsDependOnViewer()
    {
        users.Detail = Detail() with { ManagesRoles = true, Status = UserStatus.Active };
        subscriptions.Response = new SubscriptionMeResponse("Free", null,
            new SubscriptionUsageResponse(0, 1, DateTime.UtcNow), new SubscriptionSavedTripsResponse(0, 1));

        var asAdmin = await Service().GetUserAsync(UserId, Admin);
        var asSupport = await Service().GetUserAsync(UserId, SupportAgent);

        Assert.True(asAdmin!.ManagesRoles);
        Assert.True(asAdmin.CanLock);
        Assert.False(asSupport!.CanLock);
        Assert.False(asSupport.CanUnlock);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Lock_MissingReason_IsRejectedBeforeTransaction(string? reason)
    {
        var result = await Service().LockAsync(UserId, new LockUserRequest(reason), ActorId);

        Assert.Equal(AdminUserLockResultStatus.InvalidReason, result.Status);
        Assert.Empty(executor.LockNames);
    }

    [Fact]
    public async Task Lock_ReasonOverLimit_IsRejected_ButLimitAfterTrimIsAccepted()
    {
        roles.Users[UserId] = Member(UserId, UserStatus.Active, SystemRoles.UserName, isSystem: true);
        var atLimit = new string('x', AdminUserService.MaxLockReasonLength);

        var tooLong = await Service().LockAsync(UserId, new LockUserRequest(atLimit + "x"), ActorId);
        var trimmed = await Service().LockAsync(UserId, new LockUserRequest($"  {atLimit}  "), ActorId);

        Assert.Equal(AdminUserLockResultStatus.InvalidReason, tooLong.Status);
        Assert.Equal(AdminUserLockResultStatus.Success, trimmed.Status);
        Assert.Equal(atLimit, users.Locks.Single().Reason);
    }

    [Fact]
    public async Task Lock_Self_IsRejectedBeforeTransaction()
    {
        var result = await Service().LockAsync(ActorId, new LockUserRequest("test"), ActorId);

        Assert.Equal(AdminUserLockResultStatus.CannotLockSelf, result.Status);
        Assert.Empty(executor.LockNames);
    }

    [Fact]
    public async Task LockAndUnlock_UnknownUser_ReturnsUserNotFound()
    {
        var service = Service();

        Assert.Equal(AdminUserLockResultStatus.UserNotFound,
            (await service.LockAsync(UserId, new LockUserRequest("test"), ActorId)).Status);
        Assert.Equal(AdminUserLockResultStatus.UserNotFound, (await service.UnlockAsync(UserId, ActorId)).Status);
    }

    [Fact]
    public async Task Lock_ActiveUser_WritesTrimmedReasonActorAndTime_UnderRoleManagementLock()
    {
        roles.Users[UserId] = Member(UserId, UserStatus.Active, SystemRoles.UserName, isSystem: true);

        var result = await Service().LockAsync(UserId, new LockUserRequest("  spam  "), ActorId);

        Assert.Equal(AdminUserLockResultStatus.Success, result.Status);
        Assert.Equal("Locked", result.Response!.Status);
        Assert.Equal(new LockCall(UserId, "spam", ActorId, Now.UtcDateTime), Assert.Single(users.Locks));
        Assert.Equal([AdminLocks.RoleManagement], executor.LockNames);
    }

    [Fact]
    public async Task Lock_AlreadyLocked_SucceedsWithoutWriting()
    {
        roles.Users[UserId] = Member(UserId, UserStatus.Locked, SystemRoles.UserName, isSystem: true);

        var result = await Service().LockAsync(UserId, new LockUserRequest("again"), ActorId);

        Assert.Equal(AdminUserLockResultStatus.Success, result.Status);
        Assert.Empty(users.Locks);
    }

    [Fact]
    public async Task LockAndUnlock_RoleManagerByActorWithoutManageRoles_IsForbidden()
    {
        roles.Users[UserId] = Member(UserId, UserStatus.Active, SystemRoles.AdminName, isSystem: true);
        roles.Users[ActorId] = Member(ActorId, UserStatus.Active, "CSKH", isSystem: false, Permissions.ManageUsers);
        var service = Service();

        Assert.Equal(AdminUserLockResultStatus.CannotManageRoleManager,
            (await service.LockAsync(UserId, new LockUserRequest("test"), ActorId)).Status);

        roles.Users[UserId] = Member(UserId, UserStatus.Locked, SystemRoles.AdminName, isSystem: true);
        Assert.Equal(AdminUserLockResultStatus.CannotManageRoleManager,
            (await service.UnlockAsync(UserId, ActorId)).Status);
        Assert.Empty(users.Locks);
        Assert.Equal(0, users.Unlocks);
    }

    [Fact]
    public async Task Lock_RoleManagerByAnotherManager_ChecksRemainingManagers()
    {
        roles.Users[UserId] = Member(UserId, UserStatus.Active, "Trưởng nhóm", isSystem: false, Permissions.ManageRoles);
        roles.Users[ActorId] = Member(ActorId, UserStatus.Active, SystemRoles.AdminName, isSystem: true);
        roles.RemainingManagers = 0;

        var blocked = await Service().LockAsync(UserId, new LockUserRequest("test"), ActorId);

        Assert.Equal(AdminUserLockResultStatus.LastRoleManager, blocked.Status);
        Assert.Equal(UserId, roles.LastExcludedUserId);
        Assert.Empty(users.Locks);

        roles.RemainingManagers = 1;
        Assert.Equal(AdminUserLockResultStatus.Success,
            (await Service().LockAsync(UserId, new LockUserRequest("test"), ActorId)).Status);
    }

    [Fact]
    public async Task Unlock_LockedUser_Writes_ActiveUser_DoesNot()
    {
        roles.Users[UserId] = Member(UserId, UserStatus.Locked, SystemRoles.UserName, isSystem: true);
        Assert.Equal(AdminUserLockResultStatus.Success, (await Service().UnlockAsync(UserId, ActorId)).Status);
        Assert.Equal(1, users.Unlocks);

        roles.Users[UserId] = Member(UserId, UserStatus.Active, SystemRoles.UserName, isSystem: true);
        Assert.Equal(AdminUserLockResultStatus.Success, (await Service().UnlockAsync(UserId, ActorId)).Status);
        Assert.Equal(1, users.Unlocks);
        Assert.Equal([AdminLocks.RoleManagement, AdminLocks.RoleManagement], executor.LockNames);
    }

    private static UserRoleReadModel Member(Guid userId, UserStatus status, string roleName, bool isSystem,
        params string[] permissions) =>
        new(userId, Guid.NewGuid(), status, SystemRoles.Normalize(roleName), isSystem, permissions);

    private static AdminUserListItemResponse ListItem(Guid id, string status, bool managesRoles) => new(
        id, "Tên", $"{id:N}@test.dev", new AdminUserRoleResponse(Guid.NewGuid(), "Role"), status, null,
        new AdminUserPlanResponse("Free", "Free"), null, DateTime.UtcNow, managesRoles);

    private static AdminUserDetailReadModel Detail() => new(
        UserId, "An", "an@test.dev", new AdminUserRoleResponse(Guid.NewGuid(), "User"), false, UserStatus.Locked,
        DateTime.UtcNow, "spam", null, false, ["Google"], new AdminUserStatsResponse(0, 0, 0, 0, 0, 0),
        DateTime.UtcNow, DateTime.UtcNow);

    private sealed record LockCall(Guid UserId, string Reason, Guid LockedBy, DateTime LockedAt);

    private sealed class FakeAdminUserRepository : IAdminUserRepository
    {
        public bool Exists { get; set; }
        public AdminUserDetailReadModel? Detail { get; set; }
        public int TripQueries { get; private set; }
        public List<AdminUserListItemResponse> Items { get; set; } = [];
        public List<LockCall> Locks { get; } = [];
        public int Unlocks { get; private set; }

        public Task<PagedResult<AdminUserListItemResponse>> GetPagedAsync(AdminUserQuery query, DateTime nowUtc,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(PagedResult<AdminUserListItemResponse>.Create(Items, query.Page, query.PageSize, Items.Count));

        public Task<AdminUserFilterOptionsResponse> GetFilterOptionsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Exists);

        public Task<AdminUserDetailReadModel?> GetDetailAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Detail);

        public Task<PagedResult<AdminUserTripResponse>> GetTripsAsync(Guid userId, AdminUserTripQuery query,
            CancellationToken cancellationToken = default)
        {
            TripQueries++;
            return Task.FromResult(PagedResult<AdminUserTripResponse>.Create([], query.Page, query.PageSize, 0));
        }

        public Task<AdminUserLockResponse?> GetLockStateAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            var locked = Locks.LastOrDefault(call => call.UserId == userId);
            return Task.FromResult<AdminUserLockResponse?>(locked is null
                ? new AdminUserLockResponse(userId, "Active", null, null, null)
                : new AdminUserLockResponse(userId, "Locked", locked.LockedAt, locked.Reason, null));
        }

        public Task LockAsync(Guid userId, string reason, Guid lockedByUserId, DateTime lockedAtUtc,
            CancellationToken cancellationToken = default)
        {
            Locks.Add(new LockCall(userId, reason, lockedByUserId, lockedAtUtc));
            return Task.CompletedTask;
        }

        public Task UnlockAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            Unlocks++;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRoleRepository : IAdminRoleRepository
    {
        public Dictionary<Guid, UserRoleReadModel> Users { get; } = [];
        public int RemainingManagers { get; set; } = 1;
        public Guid? LastExcludedUserId { get; private set; }

        public Task<UserRoleReadModel?> GetUserRoleAsync(Guid userId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Users.GetValueOrDefault(userId));

        public Task<int> CountActiveRoleManagersAsync(Guid? excludeUserId, Guid? excludeRoleId,
            CancellationToken cancellationToken = default)
        {
            LastExcludedUserId = excludeUserId;
            return Task.FromResult(RemainingManagers);
        }

        public Task<IReadOnlyList<RoleReadModel>> GetAllAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RoleReadModel?> GetByIdAsync(Guid roleId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Role?> GetForUpdateAsync(Guid roleId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> NormalizedNameExistsAsync(string normalizedName, Guid? excludeRoleId,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<bool> TryAddAsync(Role role, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> TrySaveAsync(Role role, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(Role role, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> CountUsersAsync(Guid roleId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetUserRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class FakeOperationExecutor : IAdminOperationExecutor
    {
        public List<string> LockNames { get; } = [];

        public Task<T> ExecuteExclusiveAsync<T>(string lockName, Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken = default)
        {
            LockNames.Add(lockName);
            return operation(cancellationToken);
        }
    }

    private sealed class FakeTransactionRepository : IAdminTransactionRepository
    {
        public List<AdminTransactionFilter> Filters { get; } = [];
        public PagedQuery? LastPaging { get; private set; }

        public Task<PagedResult<AdminTransactionResponse>> GetTransactionsAsync(AdminTransactionFilter filter,
            PagedQuery paging, CancellationToken cancellationToken = default)
        {
            Filters.Add(filter);
            LastPaging = paging;
            return Task.FromResult(PagedResult<AdminTransactionResponse>.Create([], paging.Page, paging.PageSize, 0));
        }

        public Task<AdminTransactionDetailResponse?> GetDetailAsync(Guid id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AdminTransactionSummary> GetSummaryAsync(AdminTransactionFilter filter,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();

        public Task<AdminTransactionExportRows> GetExportRowsAsync(AdminTransactionFilter filter, int maxRows,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class FakeSubscriptionService : ISubscriptionService
    {
        public SubscriptionMeResponse? Response { get; set; }
        public int Calls { get; private set; }
        public Guid? LastUserId { get; private set; }

        public Task<IReadOnlyList<SubscriptionPlanResponse>> GetPlansAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<SubscriptionMeResponse?> GetMySubscriptionAsync(Guid userId,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastUserId = userId;
            return Task.FromResult(Response);
        }
    }
}
