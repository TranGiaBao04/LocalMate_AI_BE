using LocalMateAI.Application.DTOs.Auth;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IRoleRepository
{
    /// <summary>Role hệ thống theo tên chuẩn hoá (vd. "USER"); null nếu DB chưa có (migration chưa chạy).</summary>
    Task<SystemRoleReference?> GetSystemRoleAsync(
        string normalizedName,
        CancellationToken cancellationToken = default);
}
