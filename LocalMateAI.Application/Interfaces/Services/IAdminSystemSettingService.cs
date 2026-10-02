using LocalMateAI.Application.DTOs.Settings;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IAdminSystemSettingService
{
    Task<IReadOnlyList<SystemSettingResponse>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<AdminSystemSettingResult> UpdateAsync(string key, UpdateSystemSettingRequest request, Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<AdminSystemSettingResult> ResetAsync(string key, CancellationToken cancellationToken = default);
}
