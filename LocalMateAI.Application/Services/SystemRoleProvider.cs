using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Common;
using Microsoft.Extensions.Caching.Memory;

namespace LocalMateAI.Application.Services;

/// <summary>
/// BE-82: tra Id của role hệ thống theo tên rồi cache, để đăng ký/đăng nhập không phải hỏi DB mỗi lần.
/// Role hệ thống không đổi tên/không xoá; cache 1 giờ để nếu DB bị dựng lại lúc app đang chạy thì tự đúng lại.
/// </summary>
public sealed class SystemRoleProvider(
    IRoleRepository roleRepository,
    IMemoryCache memoryCache) : ISystemRoleProvider
{
    public static readonly TimeSpan CacheDuration = TimeSpan.FromHours(1);

    public Task<SystemRoleReference> GetUserRoleAsync(CancellationToken cancellationToken = default) =>
        GetAsync(SystemRoles.UserName, cancellationToken);

    public Task<SystemRoleReference> GetAdminRoleAsync(CancellationToken cancellationToken = default) =>
        GetAsync(SystemRoles.AdminName, cancellationToken);

    private async Task<SystemRoleReference> GetAsync(string roleName, CancellationToken cancellationToken)
    {
        var cacheKey = $"system-role:{roleName}";
        if (memoryCache.TryGetValue(cacheKey, out SystemRoleReference? cached) && cached is not null)
        {
            return cached;
        }

        var role = await roleRepository.GetSystemRoleAsync(SystemRoles.Normalize(roleName), cancellationToken)
            ?? throw new InvalidOperationException(
                $"Không tìm thấy role hệ thống '{roleName}' trong DB (migration AddRbacAndUserStatus chưa chạy?).");

        memoryCache.Set(cacheKey, role, CacheDuration);
        return role;
    }
}
