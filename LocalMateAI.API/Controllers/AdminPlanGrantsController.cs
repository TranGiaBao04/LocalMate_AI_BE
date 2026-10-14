using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/admin/users/{userId:guid}/grants")]
[HasPermission(Permissions.ManagePlans)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AdminPlanGrantsController(IPlanGrantService planGrantService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<PlanGrantResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var grants = await planGrantService.GetByUserAsync(userId, cancellationToken);
        return grants is null ? NotFound(CreateUserNotFoundProblem()) : Ok(grants);
    }

    [HttpPost]
    [ProducesResponseType<PlanGrantResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GrantAsync(
        Guid userId,
        [FromBody] GrantPlanRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(AuthClaimNames.Subject)?.Value, out var actorUserId)
            || actorUserId == Guid.Empty)
        {
            return Unauthorized(CreateProblem(StatusCodes.Status401Unauthorized,
                "Authentication identity is invalid.", "invalid_identity"));
        }

        var result = await planGrantService.GrantAsync(userId, request, actorUserId, cancellationToken);
        return result.Status switch
        {
            PlanGrantResultStatus.Success => StatusCode(StatusCodes.Status201Created, result.Response),
            PlanGrantResultStatus.InvalidRequest => BadRequest(CreateInvalidGrantProblem(result.ValidationErrors!)),
            PlanGrantResultStatus.UserNotFound => NotFound(CreateUserNotFoundProblem()),
            PlanGrantResultStatus.UserLocked => Conflict(CreateProblem(StatusCodes.Status409Conflict,
                "User is locked.", "user_locked", "Không thể tặng gói cho tài khoản đang bị khoá.")),
            PlanGrantResultStatus.ExceedsMaxHorizon => Conflict(CreateProblem(StatusCodes.Status409Conflict,
                "Grant exceeds the maximum horizon.", "grant_exceeds_max_horizon",
                $"Gói tặng không được kết thúc sau {PlanGrantRules.MaxHorizonDays} ngày kể từ hôm nay.")),
            _ => throw new InvalidOperationException("Unknown plan grant result.")
        };
    }

    private ValidationProblemDetails CreateInvalidGrantProblem(IReadOnlyDictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors.ToDictionary(error => error.Key, error => error.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Plan grant is invalid.",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "invalid_grant";
        return problem;
    }

    private ProblemDetails CreateUserNotFoundProblem() =>
        CreateProblem(StatusCodes.Status404NotFound, "User not found.", "user_not_found");

    private ProblemDetails CreateProblem(int status, string title, string code, string? detail = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Instance = HttpContext.Request.Path };
        problem.Extensions["code"] = code;
        return problem;
    }
}
