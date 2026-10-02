using LocalMateAI.Application.DTOs.Roles;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IAdminRoleRepository
{
    Task<IReadOnlyList<RoleReadModel>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<RoleReadModel?> GetByIdAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Role đang được theo dõi (tracked), kèm Permissions, để sửa/xoá.</summary>
    Task<Role?> GetForUpdateAsync(Guid roleId, CancellationToken cancellationToken = default);

    Task<bool> NormalizedNameExistsAsync(
        string normalizedName,
        Guid? excludeRoleId,
        CancellationToken cancellationToken = default);

    /// <summary>false nếu trùng tên (unique NormalizedName).</summary>
    Task<bool> TryAddAsync(Role role, CancellationToken cancellationToken = default);

    /// <summary>Lưu thay đổi của role đã lấy bằng GetForUpdateAsync; false nếu trùng tên.</summary>
    Task<bool> TrySaveAsync(Role role, CancellationToken cancellationToken = default);

    Task DeleteAsync(Role role, CancellationToken cancellationToken = default);

    Task<int> CountUsersAsync(Guid roleId, CancellationToken cancellationToken = default);

    /// <summary>Số user Active có quyền ManageRoles, bỏ qua một user và/hoặc mọi user của một role.</summary>
    Task<int> CountActiveRoleManagersAsync(
        Guid? excludeUserId,
        Guid? excludeRoleId,
        CancellationToken cancellationToken = default);

    Task<UserRoleReadModel?> GetUserRoleAsync(Guid userId, CancellationToken cancellationToken = default);

    Task SetUserRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken = default);
}
