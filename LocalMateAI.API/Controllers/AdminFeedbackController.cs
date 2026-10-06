using FluentValidation;
using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.AdminFeedback;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Dashboard;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/admin")]
[HasPermission(Permissions.ViewFeedback)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AdminFeedbackController(IAdminFeedbackService service) : ControllerBase
{
    [HttpGet("reviews")]
    [ProducesResponseType<PagedResult<AdminReviewResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetReviewsAsync(
        [FromQuery] AdminReviewQuery query,
        [FromServices] IValidator<AdminReviewQuery> validator,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(query, cancellationToken);
        return validation.IsValid
            ? Ok(await service.GetReviewsAsync(query, cancellationToken))
            : BadRequest(CreateInvalidQueryProblem(validation.ToDictionary()));
    }

    [HttpGet("feedback")]
    [ProducesResponseType<PagedResult<AdminTripFeedbackResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetTripFeedbackAsync(
        [FromQuery] AdminTripFeedbackQuery query,
        [FromServices] IValidator<AdminTripFeedbackQuery> validator,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(query, cancellationToken);
        return validation.IsValid
            ? Ok(await service.GetTripFeedbackAsync(query, cancellationToken))
            : BadRequest(CreateInvalidQueryProblem(validation.ToDictionary()));
    }

    [HttpGet("feedback/summary")]
    [ProducesResponseType<AdminFeedbackSummaryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSummaryAsync(
        [FromQuery] DashboardDateRangeQuery query,
        [FromServices] IValidator<DashboardDateRangeQuery> validator,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(query, cancellationToken);
        return validation.IsValid
            ? Ok(await service.GetSummaryAsync(query, cancellationToken))
            : BadRequest(CreateInvalidQueryProblem(validation.ToDictionary()));
    }

    private ValidationProblemDetails CreateInvalidQueryProblem(IDictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Tham số danh sách phản hồi không hợp lệ.",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "invalid_admin_feedback_query";
        return problem;
    }
}
