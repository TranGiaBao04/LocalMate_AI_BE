using System.Globalization;
using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/trips")]
public sealed class TripExplanationsController(ITripExplanationService explanationService) : ControllerBase
{
    /// <summary>
    /// Nhờ AI viết lý do cho từng chặng của một trip bản nháp. AI chỉ viết chữ: không đổi địa điểm, thứ tự hay giờ.
    /// Lỗi/quá giờ trả 503 và trip giữ nguyên câu lý do cũ.
    /// </summary>
    [HttpPost("{tripId:guid}/explanations")]
    [Authorize(Policy = AppPolicies.RegisteredUser)]
    [ProducesResponseType<ExplainTripResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ExplainTripResponse>> ExplainAsync(Guid tripId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId) || userId == Guid.Empty)
        {
            return Unauthorized(CreateProblem(
                StatusCodes.Status401Unauthorized, "Authentication identity is invalid.", "invalid_identity"));
        }

        var result = await explanationService.ExplainAsync(userId, tripId, cancellationToken);

        return result.Status switch
        {
            ExplainTripResultStatus.Success => Ok(result.Response),
            ExplainTripResultStatus.InvalidId =>
                BadRequest(CreateProblem(StatusCodes.Status400BadRequest, "Trip ID is invalid.", "invalid_id")),
            ExplainTripResultStatus.UserNotFound =>
                StatusCode(StatusCodes.Status403Forbidden, CreateProblem(
                    StatusCodes.Status403Forbidden,
                    "A persisted user account is required to use AI features.",
                    "ai_requires_persisted_user")),
            ExplainTripResultStatus.TripNotFound =>
                NotFound(CreateProblem(StatusCodes.Status404NotFound, "Trip was not found.", "trip_not_found")),
            ExplainTripResultStatus.TripFinalized =>
                Conflict(CreateProblem(
                    StatusCodes.Status409Conflict,
                    "Trip is finalized and its timeline is locked.",
                    "trip_finalized")),
            ExplainTripResultStatus.TripLimitReached =>
                StatusCode(StatusCodes.Status429TooManyRequests, CreateProblem(
                    StatusCodes.Status429TooManyRequests,
                    "AI đã viết lại lý do cho chuyến đi này đủ số lần cho phép.",
                    "ai_trip_limit_reached")),
            ExplainTripResultStatus.DailyLimitReached =>
                StatusCode(StatusCodes.Status429TooManyRequests, CreateProblem(
                    StatusCodes.Status429TooManyRequests,
                    "Bạn đã dùng hết lượt AI của hôm nay.",
                    "ai_daily_limit_reached",
                    result.ResetAtUtc)),
            ExplainTripResultStatus.CoolingDown => CreateCooldownResult(result.RetryAfterSeconds),
            ExplainTripResultStatus.AiUnavailable =>
                CreateUnavailableResult(
                    "Tính năng AI tạm thời chưa dùng được. Lịch trình của bạn không bị thay đổi.",
                    result.Charged,
                    result.RetryAfterSeconds),
            _ => throw new InvalidOperationException("Unsupported trip explanation result status.")
        };
    }

    // retryAfterSeconds > 0 khi lần lỗi này bắt đầu thời gian chờ: client khoá nút ngay, không cần gọi lại để biết.
    private ObjectResult CreateUnavailableResult(string title, bool charged, int retryAfterSeconds)
    {
        var problem = CreateProblem(
            StatusCodes.Status503ServiceUnavailable, title, "ai_unavailable", charged: charged);

        if (retryAfterSeconds > 0)
        {
            Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);
            problem.Extensions["retryAfterSeconds"] = retryAfterSeconds;
        }

        return new ObjectResult(problem) { StatusCode = StatusCodes.Status503ServiceUnavailable };
    }

    private ObjectResult CreateCooldownResult(int retryAfterSeconds)
    {
        Response.Headers.RetryAfter = retryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        var problem = CreateProblem(
            StatusCodes.Status429TooManyRequests,
            "AI vừa gặp lỗi. Bạn vui lòng thử lại sau ít giây.",
            "ai_cooldown");
        problem.Extensions["retryAfterSeconds"] = retryAfterSeconds;

        return new ObjectResult(problem) { StatusCode = StatusCodes.Status429TooManyRequests };
    }

    private ProblemDetails CreateProblem(
        int status, string title, string code, DateTime? resetAt = null, bool? charged = null)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Type = $"https://httpstatuses.com/{status}",
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["code"] = code;

        if (resetAt is { } value)
        {
            problem.Extensions["resetAt"] = value;
        }

        if (charged is { } wasCharged)
        {
            problem.Extensions["charged"] = wasCharged;
        }

        return problem;
    }
}
