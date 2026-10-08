using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Notifications;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize(Policy = AppPolicies.RegisteredUser)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class NotificationsController(INotificationService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<NotificationResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAsync(
        [FromQuery] NotificationQuery query,
        CancellationToken cancellationToken)
    {
        if (ResolveUser(out var userId) is { } failure)
        {
            return failure;
        }

        var result = await service.GetNotificationsAsync(userId, query, cancellationToken);
        if (result.Status == NotificationListResultStatus.Success)
        {
            return Ok(result.Response);
        }

        var problem = new ValidationProblemDetails(result.ValidationErrors!.ToDictionary(e => e.Key, e => e.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Tham số danh sách thông báo không hợp lệ.",
            Instance = Request.Path
        };
        problem.Extensions["code"] = "invalid_notification_query";
        return BadRequest(problem);
    }

    [HttpGet("unread-count")]
    [ProducesResponseType<UnreadNotificationCountResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnreadCountAsync(CancellationToken cancellationToken)
    {
        if (ResolveUser(out var userId) is { } failure)
        {
            return failure;
        }

        return Ok(await service.GetUnreadCountAsync(userId, cancellationToken));
    }

    [HttpPost("{id:guid}/read")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> MarkReadAsync(Guid id, CancellationToken cancellationToken)
    {
        if (ResolveUser(out var userId) is { } failure)
        {
            return failure;
        }

        return await service.MarkReadAsync(userId, id, cancellationToken)
            ? NoContent()
            : NotFound(CreateProblem(
                StatusCodes.Status404NotFound,
                "Không tìm thấy thông báo.",
                "notification_not_found"));
    }

    [HttpPost("read-all")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> MarkAllReadAsync(CancellationToken cancellationToken)
    {
        if (ResolveUser(out var userId) is { } failure)
        {
            return failure;
        }

        await service.MarkAllReadAsync(userId, cancellationToken);
        return NoContent();
    }

    // Trả null khi xác định được người dùng thật; ngược lại trả sẵn phản hồi lỗi.
    private IActionResult? ResolveUser(out Guid userId)
    {
        userId = Guid.Empty;

        // Phiên demo không có tài khoản thật nên không có hộp thư.
        if (User.FindFirst(AuthClaimNames.SessionType)?.Value == AuthClaimNames.DemoSession)
        {
            return StatusCode(StatusCodes.Status403Forbidden, CreateProblem(
                StatusCodes.Status403Forbidden,
                "Cần tài khoản thật để dùng hộp thư thông báo.",
                "notifications_requires_persisted_user"));
        }

        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out userId) || userId == Guid.Empty)
        {
            return Unauthorized(CreateProblem(
                StatusCodes.Status401Unauthorized,
                "Thông tin đăng nhập không hợp lệ.",
                "invalid_identity"));
        }

        return null;
    }

    private ProblemDetails CreateProblem(int status, string title, string code)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://httpstatuses.com/{status}",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = code;
        return problem;
    }
}
