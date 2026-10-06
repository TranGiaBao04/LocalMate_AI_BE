namespace LocalMateAI.Application.Security;

/// <summary>BE-82: một quyền mà role có thể được cấp. Tên/mô tả tiếng Việt để FE hiện checkbox.</summary>
public sealed record PermissionDefinition(string Code, string Name, string Description);

/// <summary>
/// BE-82: danh sách quyền cố định trong code (quyền mới = thêm hằng số + deploy).
/// Role nào có quyền nào lưu trong bảng RolePermissions; role hệ thống Admin có mọi quyền theo luật cứng.
/// </summary>
public static class Permissions
{
    public const string ManagePlaces = nameof(ManagePlaces);
    public const string ManagePlans = nameof(ManagePlans);
    public const string ViewRevenue = nameof(ViewRevenue);
    public const string ManageUsers = nameof(ManageUsers);
    public const string ManageRoles = nameof(ManageRoles);
    public const string ManageSettings = nameof(ManageSettings);
    public const string ViewFeedback = nameof(ViewFeedback);

    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(ManagePlaces, "Quản lý địa điểm", "Tạo, sửa, xoá, duyệt địa điểm; xem ga Metro; import địa điểm."),
        new(ManagePlans, "Quản lý gói", "Sửa gói và giá; cấp quyền thủ công, hoàn tiền, đối soát giao dịch."),
        new(ViewRevenue, "Xem doanh thu", "Xem dashboard, danh sách và tổng hợp giao dịch."),
        new(ManageUsers, "Quản lý người dùng", "Xem, khoá và mở khoá tài khoản."),
        new(ManageRoles, "Quản lý phân quyền", "Tạo, sửa, xoá role và gán role cho người dùng."),
        new(ManageSettings, "Cấu hình hệ thống", "Xem và sửa các thông số vận hành (ví dụ ngưỡng địa điểm mỗi ga)."),
        new(ViewFeedback, "Xem phản hồi & đánh giá", "Xem đánh giá địa điểm và phản hồi chuyến đi của người dùng.")
    ];

    public static bool IsDefined(string code) => All.Any(permission => permission.Code == code);
}
