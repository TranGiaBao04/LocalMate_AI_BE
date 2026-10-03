using LocalMateAI.Application.DTOs.Users;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

/// <summary>
/// BE-134: ai được khoá/mở khoá ai. Dùng cho cờ canLock/canUnlock (để FE ẩn nút) và cho chính thao tác khoá,
/// để hai nơi không lệch nhau. Không xét "người quản lý role cuối cùng": người xem có ManageRoles luôn còn chính họ.
/// </summary>
public static class AdminUserLockRules
{
    public static bool CanManage(bool targetManagesRoles, bool actorManagesRoles) =>
        !targetManagesRoles || actorManagesRoles;

    public static bool CanLock(Guid targetUserId, UserStatus targetStatus, bool targetManagesRoles, AdminActor actor) =>
        targetUserId != actor.UserId
        && targetStatus == UserStatus.Active
        && CanManage(targetManagesRoles, actor.ManagesRoles);

    public static bool CanUnlock(UserStatus targetStatus, bool targetManagesRoles, AdminActor actor) =>
        targetStatus == UserStatus.Locked && CanManage(targetManagesRoles, actor.ManagesRoles);
}
