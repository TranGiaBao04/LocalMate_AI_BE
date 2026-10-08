using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Users;

/// <summary>BE-132: lọc / phân trang / tìm kiếm cho <c>GET /api/admin/users</c>.</summary>
public sealed record AdminUserQuery : PagedQuery
{
    public const int MaxPlanLength = 64;

    public static readonly IReadOnlyCollection<string> SortFields = ["createdAt", "fullName", "email", "status"];

    public Guid? RoleId { get; init; }

    public UserStatus? Status { get; init; }

    /// <summary>Mã gói hiện tại (Free, TripPass, Membership hoặc mã gói admin tạo), không phân biệt hoa thường.</summary>
    public string? Plan { get; init; }
}

public sealed record AdminUserRoleResponse(Guid Id, string Name);

public sealed record AdminUserPlanResponse(string Code, string Name);

/// <summary>
/// BE-132: một dòng danh sách user. <c>PlanEffectiveUntil</c> = hết kỳ đang hiệu lực (giống <c>effectiveUntil</c>
/// của /subscription/me, chưa cộng các kỳ gia hạn nối tiếp); gói Free ⇒ null.
/// </summary>
public sealed record AdminUserListItemResponse(
    Guid Id,
    string FullName,
    string Email,
    AdminUserRoleResponse Role,
    string Status,
    DateTime? LockedAt,
    AdminUserPlanResponse Plan,
    DateTime? PlanEffectiveUntil,
    DateTime CreatedAt,
    bool ManagesRoles)
{
    /// <summary>Người đang xem có được khoá user này không (theo <c>AdminUserLockRules</c>).</summary>
    public bool CanLock { get; init; }

    /// <summary>Người đang xem có được mở khoá user này không.</summary>
    public bool CanUnlock { get; init; }
}

/// <summary>Người đang gọi API admin: dùng để tính canLock/canUnlock.</summary>
public sealed record AdminActor(Guid UserId, bool ManagesRoles);

public sealed record AdminUserPlanOptionResponse(string Code, string Name, bool IsActive);

/// <summary>BE-132: dữ liệu cho ô lọc Role/Gói ở màn Users (không cần quyền ManageRoles/ManagePlans).</summary>
public sealed record AdminUserFilterOptionsResponse(
    IReadOnlyList<AdminUserRoleResponse> Roles,
    IReadOnlyList<AdminUserPlanOptionResponse> Plans);

/// <summary>BE-133: lọc / phân trang lịch sử chuyến đi của một user (bỏ qua <c>search</c>).</summary>
public sealed record AdminUserTripQuery : PagedQuery
{
    public static readonly IReadOnlyCollection<string> SortFields = ["createdAt", "plannedStartAt", "finalizedAt"];

    public TripStatus? Status { get; init; }

    /// <summary>true ⇒ gồm cả trip user đã xoá mềm (có <c>deletedAt</c>).</summary>
    public bool IncludeDeleted { get; init; }
}

public sealed record AdminUserLockedByResponse(Guid UserId, string FullName, string Email);

/// <summary>
/// BE-133: số liệu tổng hợp. TripCount/FinalizedTripCount chỉ tính trip chưa xoá; TotalPaid = tổng Amount của đơn Paid
/// (gồm cả mua lẻ, chưa trừ hoàn tiền); UnusedSingleItineraryCount = lượt mua lẻ chưa dùng.
/// </summary>
public sealed record AdminUserStatsResponse(
    int TripCount,
    int FinalizedTripCount,
    int DeletedTripCount,
    int PaidOrderCount,
    decimal TotalPaid,
    int UnusedSingleItineraryCount);

/// <summary>BE-133: dữ liệu chi tiết đọc từ DB, chưa có phần gói (service ghép từ ISubscriptionService).</summary>
public sealed record AdminUserDetailReadModel(
    Guid Id,
    string FullName,
    string Email,
    AdminUserRoleResponse Role,
    bool ManagesRoles,
    UserStatus Status,
    DateTime? LockedAt,
    string? LockReason,
    AdminUserLockedByResponse? LockedBy,
    bool HasPassword,
    IReadOnlyList<string> LoginProviders,
    AdminUserStatsResponse Stats,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary>BE-133: chi tiết user. <c>Subscription</c> giống hệt <c>GET /api/subscription/me</c> của chính user đó.</summary>
public sealed record AdminUserDetailResponse(
    Guid Id,
    string FullName,
    string Email,
    AdminUserRoleResponse Role,
    bool ManagesRoles,
    string Status,
    DateTime? LockedAt,
    string? LockReason,
    AdminUserLockedByResponse? LockedBy,
    bool HasPassword,
    IReadOnlyList<string> LoginProviders,
    SubscriptionMeResponse Subscription,
    AdminUserStatsResponse Stats,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public bool CanLock { get; init; }

    public bool CanUnlock { get; init; }
}

/// <summary>BE-133: một chuyến đi trong lịch sử của user. Không trả toạ độ xuất phát.</summary>
public sealed record AdminUserTripResponse(
    Guid Id,
    string Status,
    DateOnly? PlannedDate,
    TimeOnly? StartTime,
    int DurationHours,
    decimal BudgetMax,
    string TravelMode,
    int ItemCount,
    decimal EstimatedBudget,
    DateTime CreatedAt,
    DateTime? FinalizedAt,
    DateTime? DeletedAt);

public sealed record LockUserRequest(string? Reason);

public sealed record AdminUserLockResponse(
    Guid UserId,
    string Status,
    DateTime? LockedAt,
    string? LockReason,
    AdminUserLockedByResponse? LockedBy);

public enum AdminUserLockResultStatus
{
    Success,
    InvalidReason,
    CannotLockSelf,
    UserNotFound,
    CannotManageRoleManager,
    LastRoleManager
}

public sealed record AdminUserLockResult(AdminUserLockResultStatus Status, AdminUserLockResponse? Response = null)
{
    public static AdminUserLockResult Succeeded(AdminUserLockResponse response) => new(AdminUserLockResultStatus.Success, response);

    public static AdminUserLockResult Failed(AdminUserLockResultStatus status) => new(status);
}
