using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Roles;

public sealed record PermissionResponse(string Code, string Name, string Description);

public sealed record AdminRoleResponse(
    Guid Id,
    string Name,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> Permissions,
    int UserCount);

/// <summary>Dùng cho cả tạo và sửa role (sửa = thay toàn bộ tên, mô tả, danh sách quyền).</summary>
public sealed record SaveRoleRequest(string? Name, string? Description, IReadOnlyList<string>? Permissions);

public sealed record AssignUserRoleRequest(Guid RoleId);

public sealed record UserRoleAssignmentResponse(Guid UserId, Guid RoleId, string RoleName);

/// <summary>Dữ liệu role đọc từ DB (quyền thô, chưa áp luật Admin = mọi quyền).</summary>
public sealed record RoleReadModel(
    Guid Id,
    string Name,
    string NormalizedName,
    string? Description,
    bool IsSystem,
    IReadOnlyList<string> StoredPermissions,
    int UserCount);

/// <summary>Role hiện tại của một user, để kiểm luật chống tự khoá.</summary>
public sealed record UserRoleReadModel(
    Guid UserId,
    Guid RoleId,
    UserStatus Status,
    string RoleNormalizedName,
    bool RoleIsSystem,
    IReadOnlyList<string> RoleStoredPermissions);
