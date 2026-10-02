using LocalMateAI.Application.DTOs.Settings;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface ISystemSettingRepository
{
    Task<IReadOnlyList<SystemSettingRow>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Thêm hoặc ghi đè giá trị trong một lệnh (2 admin sửa cùng lúc ⇒ người sau thắng).</summary>
    Task UpsertAsync(string key, string value, DateTime updatedAt, Guid updatedByUserId,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
