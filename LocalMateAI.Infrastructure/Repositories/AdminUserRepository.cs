using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Users;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Security;
using LocalMateAI.Application.Services;
using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;
using LocalMateAI.Infrastructure.Persistence;
using LocalMateAI.Infrastructure.Persistence.Querying;
using Microsoft.EntityFrameworkCore;

namespace LocalMateAI.Infrastructure.Repositories;

public sealed class AdminUserRepository(AppDbContext dbContext) : IAdminUserRepository
{
    private static readonly string AdminNormalizedName = SystemRoles.Normalize(SystemRoles.AdminName);

    private static readonly SortMap<UserRow> UserSortMap = new SortMap<UserRow>("createdAt", true, row => row.Id)
        .Add("createdAt", row => row.CreatedAt)
        .Add("fullName", row => row.FullName)
        .Add("email", row => row.Email)
        .Add("status", row => row.Status);

    private static readonly SortMap<TripRow> TripSortMap = new SortMap<TripRow>("createdAt", true, row => row.Id)
        .Add("createdAt", row => row.CreatedAt)
        .Add("plannedStartAt", row => row.PlannedStartAt)
        .Add("finalizedAt", row => row.FinalizedAt);

    public async Task<PagedResult<AdminUserListItemResponse>> GetPagedAsync(
        AdminUserQuery query,
        DateTime nowUtc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var rows = Rows(nowUtc);

        if (query.NormalizedSearch is { } search)
        {
            // So khớp không dấu (extension unaccent): "nguyen duc" tìm ra "Nguyễn Đức".
            var pattern = LikePattern(search);
            rows = rows.Where(row =>
                EF.Functions.ILike(EF.Functions.Unaccent(row.FullName), EF.Functions.Unaccent(pattern), "\\")
                || EF.Functions.ILike(EF.Functions.Unaccent(row.Email), EF.Functions.Unaccent(pattern), "\\"));
        }

        if (query.RoleId is { } roleId)
        {
            rows = rows.Where(row => row.RoleId == roleId);
        }

        if (query.Status is { } status)
        {
            rows = rows.Where(row => row.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Plan))
        {
            var planCode = PlanIdentity.Canonical(query.Plan)!.ToUpperInvariant();
            rows = planCode == PlanIdentity.Free
                ? rows.Where(row => row.PaidPlanCode == null)
                : rows.Where(row => row.PaidPlanCode != null && row.PaidPlanCode.ToUpper() == planCode);
        }

        var page = await rows.ApplySort(query, UserSortMap).ToPagedResultAsync(query, cancellationToken);
        if (page.Items.Count == 0)
        {
            return PagedResult<AdminUserListItemResponse>.Create([], page.Page, page.PageSize, page.TotalCount);
        }

        var freePlanName = await dbContext.SubscriptionPlans
            .AsNoTracking()
            .Where(plan => plan.Code == PlanIdentity.Free)
            .Select(plan => plan.Name)
            .SingleAsync(cancellationToken);

        var items = page.Items
            .Select(row => new AdminUserListItemResponse(
                row.Id,
                row.FullName,
                row.Email,
                new AdminUserRoleResponse(row.RoleId, row.RoleName),
                row.Status.ToString(),
                row.LockedAt,
                row.PaidPlanCode is null
                    ? new AdminUserPlanResponse(PlanIdentity.PublicCode(PlanIdentity.Free), freePlanName)
                    : new AdminUserPlanResponse(PlanIdentity.PublicCode(row.PaidPlanCode), row.PaidPlanName!),
                row.PaidPlanEffectiveUntil,
                row.CreatedAt,
                row.ManagesRoles))
            .ToList();

        return PagedResult<AdminUserListItemResponse>.Create(items, page.Page, page.PageSize, page.TotalCount);
    }

    public Task<bool> ExistsAsync(Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Users.AnyAsync(user => user.Id == userId, cancellationToken);

    public async Task<AdminUserFilterOptionsResponse> GetFilterOptionsAsync(CancellationToken cancellationToken = default)
    {
        var roles = await dbContext.Roles
            .AsNoTracking()
            .OrderByDescending(role => role.IsSystem)
            .ThenBy(role => role.Name)
            .Select(role => new AdminUserRoleResponse(role.Id, role.Name))
            .ToListAsync(cancellationToken);

        // Gồm cả gói đã tắt: user vẫn có thể đang dùng gói đó.
        var plans = await dbContext.SubscriptionPlans
            .AsNoTracking()
            .OrderBy(plan => plan.EntitlementPriority)
            .Select(plan => new { plan.Code, plan.Name, plan.IsActive })
            .ToListAsync(cancellationToken);

        return new AdminUserFilterOptionsResponse(
            roles,
            plans.Select(plan => new AdminUserPlanOptionResponse(
                PlanIdentity.PublicCode(plan.Code), plan.Name, plan.IsActive)).ToList());
    }

    public Task<AdminUserDetailReadModel?> GetDetailAsync(Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new AdminUserDetailReadModel(
                user.Id,
                user.FullName,
                user.Email,
                new AdminUserRoleResponse(user.RoleId, user.Role!.Name),
                (user.Role.IsSystem && user.Role.NormalizedName == AdminNormalizedName)
                    || user.Role.Permissions.Any(permission => permission.Permission == Permissions.ManageRoles),
                user.Status,
                user.LockedAt,
                user.LockReason,
                dbContext.Users
                    .Where(locker => locker.Id == user.LockedByUserId)
                    .Select(locker => new AdminUserLockedByResponse(locker.Id, locker.FullName, locker.Email))
                    .FirstOrDefault(),
                user.PasswordHash != null,
                dbContext.UserExternalLogins
                    .Where(login => login.UserId == user.Id)
                    .Select(login => login.Provider)
                    .OrderBy(provider => provider)
                    .ToList(),
                new AdminUserStatsResponse(
                    dbContext.Trips.Count(trip => trip.UserId == user.Id && trip.DeletedAt == null),
                    dbContext.Trips.Count(trip => trip.UserId == user.Id && trip.DeletedAt == null
                        && trip.Status == TripStatus.Finalized),
                    dbContext.Trips.Count(trip => trip.UserId == user.Id && trip.DeletedAt != null),
                    dbContext.PaymentOrders.Count(order => order.UserId == user.Id
                        && order.Status == PaymentOrderStatus.Paid),
                    dbContext.PaymentOrders
                        .Where(order => order.UserId == user.Id && order.Status == PaymentOrderStatus.Paid)
                        .Sum(order => (decimal?)order.Amount) ?? 0,
                    dbContext.SingleItineraryEntitlements.Count(entitlement => entitlement.UserId == user.Id
                        && entitlement.ConsumedAt == null)),
                user.CreatedAt,
                user.UpdatedAt))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<PagedResult<AdminUserTripResponse>> GetTripsAsync(
        Guid userId,
        AdminUserTripQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var trips = dbContext.Trips.AsNoTracking().Where(trip => trip.UserId == userId);
        if (!query.IncludeDeleted)
        {
            trips = trips.Where(trip => trip.DeletedAt == null);
        }

        if (query.Status is { } status)
        {
            trips = trips.Where(trip => trip.Status == status);
        }

        // ItemCount/EstimatedBudget tính như my-trips (TripRepository.GetByUserIdAsync).
        var page = await trips
            .Select(trip => new TripRow
            {
                Id = trip.Id,
                Status = trip.Status,
                PlannedStartAt = trip.PlannedStartAt,
                DurationHours = trip.DurationHours,
                BudgetMax = trip.BudgetMax,
                TravelMode = trip.TravelMode,
                ItemCount = trip.Items.Count,
                EstimatedBudget = trip.Items.Sum(item => item.EstimatedBudget),
                CreatedAt = trip.CreatedAt,
                FinalizedAt = trip.FinalizedAt,
                DeletedAt = trip.DeletedAt
            })
            .ApplySort(query, TripSortMap)
            .ToPagedResultAsync(query, cancellationToken);

        // PlannedStartAt là giờ đồng hồ Việt Nam (không kèm múi giờ) ⇒ tách thẳng ngày/giờ, không quy đổi.
        var items = page.Items
            .Select(row => new AdminUserTripResponse(
                row.Id,
                row.Status.ToString(),
                row.PlannedStartAt is { } plannedDate ? DateOnly.FromDateTime(plannedDate) : null,
                row.PlannedStartAt is { } startTime ? TimeOnly.FromDateTime(startTime) : null,
                row.DurationHours,
                row.BudgetMax,
                row.TravelMode.ToString(),
                row.ItemCount,
                row.EstimatedBudget,
                row.CreatedAt,
                row.FinalizedAt,
                row.DeletedAt))
            .ToList();

        return PagedResult<AdminUserTripResponse>.Create(items, page.Page, page.PageSize, page.TotalCount);
    }

    public async Task<AdminUserLockResponse?> GetLockStateAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var row = await dbContext.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new
            {
                user.Id,
                user.Status,
                user.LockedAt,
                user.LockReason,
                LockedBy = dbContext.Users
                    .Where(locker => locker.Id == user.LockedByUserId)
                    .Select(locker => new AdminUserLockedByResponse(locker.Id, locker.FullName, locker.Email))
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(cancellationToken);

        return row is null
            ? null
            : new AdminUserLockResponse(row.Id, row.Status.ToString(), row.LockedAt, row.LockReason, row.LockedBy);
    }

    // ExecuteUpdate bỏ qua ApplyAudit của AppDbContext nên tự đặt UpdatedAt.
    public Task LockAsync(Guid userId, string reason, Guid lockedByUserId, DateTime lockedAtUtc,
        CancellationToken cancellationToken = default) =>
        dbContext.Users
            .Where(user => user.Id == userId && user.Status == UserStatus.Active)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.Status, UserStatus.Locked)
                    .SetProperty(user => user.LockedAt, lockedAtUtc)
                    .SetProperty(user => user.LockReason, reason)
                    .SetProperty(user => user.LockedByUserId, lockedByUserId)
                    .SetProperty(user => user.UpdatedAt, lockedAtUtc),
                cancellationToken);

    public Task UnlockAsync(Guid userId, CancellationToken cancellationToken = default) =>
        dbContext.Users
            .Where(user => user.Id == userId && user.Status == UserStatus.Locked)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(user => user.Status, UserStatus.Active)
                    .SetProperty(user => user.LockedAt, (DateTime?)null)
                    .SetProperty(user => user.LockReason, (string?)null)
                    .SetProperty(user => user.LockedByUserId, (Guid?)null)
                    .SetProperty(user => user.UpdatedAt, DateTime.UtcNow),
                cancellationToken);

    // Gói hiện tại = luật của EffectiveSubscriptionResolver: trong các kỳ đang hiệu lực
    // (StartsAt ≤ now < min(EndsAt, TerminatedAt)), lấy gói có EntitlementPriority cao nhất (unique nên không hoà).
    // Không có kỳ nào ⇒ PaidPlanCode = null = gói Free.
    private IQueryable<UserRow> Rows(DateTime nowUtc)
    {
        var activePaidPlans =
            from period in dbContext.SubscriptionPeriods
            join plan in dbContext.SubscriptionPlans on period.PlanId equals plan.Id
            where period.StartsAt <= nowUtc
                && nowUtc < (period.TerminatedAt != null && period.TerminatedAt < period.EndsAt
                    ? period.TerminatedAt.Value
                    : period.EndsAt)
            select new
            {
                period.UserId,
                plan.Code,
                plan.Name,
                plan.EntitlementPriority,
                EffectiveEnd = period.TerminatedAt != null && period.TerminatedAt < period.EndsAt
                    ? period.TerminatedAt.Value
                    : period.EndsAt
            };

        return dbContext.Users
            .AsNoTracking()
            .Select(user => new UserRow
            {
                Id = user.Id,
                FullName = user.FullName,
                Email = user.Email,
                RoleId = user.RoleId,
                RoleName = user.Role!.Name,
                // Phải khớp RolePermissionRules và AdminRoleRepository.CountActiveRoleManagersAsync.
                ManagesRoles = (user.Role.IsSystem && user.Role.NormalizedName == AdminNormalizedName)
                    || user.Role.Permissions.Any(permission => permission.Permission == Permissions.ManageRoles),
                Status = user.Status,
                LockedAt = user.LockedAt,
                CreatedAt = user.CreatedAt,
                PaidPlanCode = activePaidPlans.Where(plan => plan.UserId == user.Id)
                    .OrderByDescending(plan => plan.EntitlementPriority)
                    .Select(plan => plan.Code)
                    .FirstOrDefault(),
                PaidPlanName = activePaidPlans.Where(plan => plan.UserId == user.Id)
                    .OrderByDescending(plan => plan.EntitlementPriority)
                    .Select(plan => plan.Name)
                    .FirstOrDefault(),
                PaidPlanEffectiveUntil = activePaidPlans.Where(plan => plan.UserId == user.Id)
                    .OrderByDescending(plan => plan.EntitlementPriority)
                    .Select(plan => (DateTime?)plan.EffectiveEnd)
                    .FirstOrDefault()
            });
    }

    private static string LikePattern(string search) =>
        "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    // Khởi tạo bằng object initializer (không dùng constructor) để EF dịch được Where/OrderBy trên các field sau Select.
    private sealed class UserRow
    {
        public Guid Id { get; init; }
        public string FullName { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public Guid RoleId { get; init; }
        public string RoleName { get; init; } = string.Empty;
        public bool ManagesRoles { get; init; }
        public UserStatus Status { get; init; }
        public DateTime? LockedAt { get; init; }
        public DateTime CreatedAt { get; init; }
        public string? PaidPlanCode { get; init; }
        public string? PaidPlanName { get; init; }
        public DateTime? PaidPlanEffectiveUntil { get; init; }
    }

    private sealed class TripRow
    {
        public Guid Id { get; init; }
        public TripStatus Status { get; init; }
        public DateTime? PlannedStartAt { get; init; }
        public int DurationHours { get; init; }
        public decimal BudgetMax { get; init; }
        public TravelMode TravelMode { get; init; }
        public int ItemCount { get; init; }
        public decimal EstimatedBudget { get; init; }
        public DateTime CreatedAt { get; init; }
        public DateTime? FinalizedAt { get; init; }
        public DateTime? DeletedAt { get; init; }
    }
}
