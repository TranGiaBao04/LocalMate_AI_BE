using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/trips")]
public sealed class TripRequestParsingController(ITripRequestParsingService parsingService) : ControllerBase
{
    /// <summary>
    /// AI đọc câu người dùng gõ và điền sẵn các tiêu chí tạo lịch. Không tạo lịch, không lưu gì:
    /// người dùng xem lại form rồi mới gọi generate. AI lỗi thì trả 503 và người dùng tự điền form.
    /// </summary>
    [HttpPost("parse-request")]
    [Authorize(Policy = AppPolicies.RegisteredUser)]
    [ProducesResponseType<ParseTripRequestResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ParseTripRequestResponse>> ParseAsync(
        [FromBody] ParseTripRequestRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var userId) || userId == Guid.Empty)
        {
            return Unauthorized(CreateProblem(
                StatusCodes.Status401Unauthorized, "Authentication identity is invalid.", "invalid_identity"));
        }

        var result = await parsingService.ParseAsync(userId, request, cancellationToken);

        return result.Status switch
        {
            ParseTripRequestResultStatus.Success => Ok(result.Response),
            ParseTripRequestResultStatus.InvalidText => CreateValidationProblem(result.ValidationErrors!),
            ParseTripRequestResultStatus.UserNotFound =>
                StatusCode(StatusCodes.Status403Forbidden, CreateProblem(
                    StatusCodes.Status403Forbidden,
                    "A persisted user account is required to use AI features.",
                    "ai_requires_persisted_user")),
            ParseTripRequestResultStatus.DailyLimitReached =>
                StatusCode(StatusCodes.Status429TooManyRequests, CreateProblem(
                    StatusCodes.Status429TooManyRequests,
                    "Bạn đã dùng hết lượt AI của hôm nay.",
                    "ai_daily_limit_reached",
                    result.ResetAtUtc)),
            ParseTripRequestResultStatus.AiUnavailable =>
                StatusCode(StatusCodes.Status503ServiceUnavailable, CreateProblem(
                    StatusCodes.Status503ServiceUnavailable,
                    "Tính năng AI tạm thời chưa dùng được. Bạn vẫn có thể tự điền thông tin chuyến đi.",
                    "ai_unavailable")),
            _ => throw new InvalidOperationException("Unsupported trip request parsing result status.")
        };
    }

    private ActionResult CreateValidationProblem(IReadOnlyDictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors.ToDictionary(pair => pair.Key, pair => pair.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Trip request text is invalid.",
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["code"] = "invalid_parse_request";

        return BadRequest(problem);
    }

    private ProblemDetails CreateProblem(int status, string title, string code, DateTime? resetAt = null)
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

        return problem;
    }
}
