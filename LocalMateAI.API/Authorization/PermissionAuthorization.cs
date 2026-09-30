using LocalMateAI.Application.DTOs.Auth;
using Microsoft.AspNetCore.Authorization;

namespace LocalMateAI.API.Authorization;

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;

/// <summary>
/// BE-82: đọc quyền từ snapshot mà AccountAccessMiddleware đã tra ở đầu request (không truy vấn DB lần 2).
/// Không có snapshot (middleware không chạy) ⇒ không đạt, trả 403.
/// </summary>
public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.Resource is HttpContext httpContext
            && httpContext.GetUserAccess() is { } access
            && access.HasPermission(requirement.Permission))
        {
            context.Succeed(requirement);
        }

        return Task.CompletedTask;
    }
}

public static class UserAccessHttpContextExtensions
{
    private const string ItemKey = "LocalMateAI.UserAccess";

    public static UserAccessSnapshot? GetUserAccess(this HttpContext httpContext) =>
        httpContext.Items.TryGetValue(ItemKey, out var value) ? value as UserAccessSnapshot : null;

    public static void SetUserAccess(this HttpContext httpContext, UserAccessSnapshot access) =>
        httpContext.Items[ItemKey] = access;
}
