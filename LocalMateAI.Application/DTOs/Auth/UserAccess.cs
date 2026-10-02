using LocalMateAI.Domain.Common;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Auth;

/// <summary>BE-83: dữ liệu thô đọc từ DB (1 truy vấn) để kiểm trạng thái + quyền mỗi request.</summary>
public sealed record UserAccessReadModel(
    UserStatus Status,
    string RoleName,
    string RoleNormalizedName,
    bool RoleIsSystem,
    IReadOnlyList<string> StoredPermissions);

/// <summary>BE-82/83: trạng thái + quyền thực tế của người đang gọi API, dùng trong 1 request.</summary>
public sealed record UserAccessSnapshot(UserStatus Status, string RoleName, IReadOnlySet<string> Permissions)
{
    // Tài khoản demo không có trong DB: coi như role User, không có quyền admin nào.
    public static readonly UserAccessSnapshot Demo =
        new(UserStatus.Active, SystemRoles.UserName, new HashSet<string>());

    public bool HasPermission(string permission) => Permissions.Contains(permission);
}
