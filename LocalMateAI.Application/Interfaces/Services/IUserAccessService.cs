using LocalMateAI.Application.DTOs.Auth;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IUserAccessService
{
    /// <summary>Trạng thái + quyền thực tế của user; null nếu user không còn trong DB.</summary>
    Task<UserAccessSnapshot?> GetSnapshotAsync(Guid userId, CancellationToken cancellationToken = default);
}
