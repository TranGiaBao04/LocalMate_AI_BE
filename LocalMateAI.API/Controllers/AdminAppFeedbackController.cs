using FluentValidation;
using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.AppFeedback;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/admin/app-feedback")]
[HasPermission(Permissions.ViewFeedback)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AdminAppFeedbackController(IAppFeedbackService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PagedResult<AdminAppFeedbackItemResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAsync(
        [FromQuery] AdminAppFeedbackQuery query,
        [FromServices] IValidator<AdminAppFeedbackQuery> validator,
        CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(query, cancellationToken);
        if (!validation.IsValid)
        {
            return BadRequest(CreateInvalidQueryProblem(validation.ToDictionary()));
        }

        var result = await service.GetAdminListAsync(query, cancellationToken);
        return Ok(result);
    }

    private ValidationProblemDetails CreateInvalidQueryProblem(IDictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Tham số danh sách góp ý không hợp lệ.",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "invalid_admin_feedback_query";
        return problem;
    }
}
