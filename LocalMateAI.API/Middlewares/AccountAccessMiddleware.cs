using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.Auth;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Middlewares;

/// <summary>
/// BE-83: chặn tài khoản bị khoá dù JWT còn hạn, và nạp quyền thực tế cho BE-82.
/// Đặt sau UseAuthentication, trước UseAuthorization. Chỉ tra DB khi endpoint yêu cầu đăng nhập.
/// </summary>
public sealed class AccountAccessMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext httpContext, IUserAccessService userAccessService)
    {
        var endpoint = httpContext.GetEndpoint();
        if (endpoint is null
            || !RequiresAuthorization(endpoint)
            || httpContext.User.Identity?.IsAuthenticated != true)
        {
            await next(httpContext);
            return;
        }

        if (httpContext.User.FindFirst(AuthClaimNames.SessionType)?.Value == AuthClaimNames.DemoSession)
        {
            httpContext.SetUserAccess(UserAccessSnapshot.Demo);
            await next(httpContext);
            return;
        }

        // sub sai định dạng: để controller trả invalid_identity như hiện tại (không có snapshot ⇒ API admin 403).
        if (!Guid.TryParse(httpContext.User.FindFirst(AuthClaimNames.Subject)?.Value, out var userId)
            || userId == Guid.Empty)
        {
            await next(httpContext);
            return;
        }

        var access = await userAccessService.GetSnapshotAsync(userId, httpContext.RequestAborted);
        if (access is null)
        {
            await WriteProblemAsync(
                httpContext,
                StatusCodes.Status401Unauthorized,
                "Account was not found.",
                "account_not_found");
            return;
        }

        if (access.Status == UserStatus.Locked)
        {
            await WriteProblemAsync(
                httpContext,
                StatusCodes.Status403Forbidden,
                "Account is locked.",
                "account_locked");
            return;
        }

        httpContext.SetUserAccess(access);
        await next(httpContext);
    }

    private static bool RequiresAuthorization(Endpoint endpoint) =>
        endpoint.Metadata.GetMetadata<IAllowAnonymous>() is null
        && endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().Count > 0;

    private static async Task WriteProblemAsync(HttpContext httpContext, int status, string title, string code)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://httpstatuses.com/{status}",
            Instance = httpContext.Request.Path
        };
        problem.Extensions["code"] = code;

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(problem, httpContext.RequestAborted);
    }
}
