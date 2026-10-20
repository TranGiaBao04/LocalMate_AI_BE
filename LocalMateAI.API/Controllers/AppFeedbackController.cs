using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.AppFeedback;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/app-feedback")]
[Authorize(Policy = AppPolicies.RegisteredUser)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AppFeedbackController(IAppFeedbackService service) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<AppFeedbackCreatedResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> SubmitAsync(
        [FromBody] CreateAppFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        if (User.FindFirst("session_type")?.Value == "demo")
        {
            return StatusCode(StatusCodes.Status403Forbidden, CreateProblem(
                StatusCodes.Status403Forbidden,
                "Phiên demo không gửi được góp ý.",
                "app_feedback_requires_persisted_user"));
        }

        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId) || userId == Guid.Empty)
        {
            return Unauthorized(CreateProblem(
                StatusCodes.Status401Unauthorized,
                "Thông tin đăng nhập không hợp lệ.",
                "invalid_identity"));
        }

        var userAgent = Request.Headers.UserAgent.ToString();
        var result = await service.SubmitFeedbackAsync(userId, request, userAgent, cancellationToken);

        return result.Status switch
        {
            CreateAppFeedbackResultStatus.Success =>
                StatusCode(StatusCodes.Status201Created, result.Response),

            CreateAppFeedbackResultStatus.InvalidInput =>
                BadRequest(CreateValidationProblem(result.ValidationErrors!)),

            CreateAppFeedbackResultStatus.DailyLimitReached =>
                StatusCode(StatusCodes.Status429TooManyRequests, CreateDailyLimitProblem(result.ResetAt!.Value)),

            _ => StatusCode(StatusCodes.Status403Forbidden, CreateProblem(
                StatusCodes.Status403Forbidden,
                "Phiên demo không gửi được góp ý.",
                "app_feedback_requires_persisted_user"))
        };
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

    private ValidationProblemDetails CreateValidationProblem(IDictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Dữ liệu góp ý không hợp lệ.",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "invalid_app_feedback";
        return problem;
    }

    private ProblemDetails CreateDailyLimitProblem(DateTime resetAt)
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Bạn đã gửi 5 góp ý hôm nay. Cảm ơn bạn, hãy quay lại vào ngày mai nhé!",
            Type = "https://httpstatuses.com/429",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "app_feedback_daily_limit_reached";
        problem.Extensions["resetAt"] = resetAt.ToString("O");
        return problem;
    }
}
