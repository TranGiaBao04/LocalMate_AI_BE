using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/subscription")]
public sealed class SubscriptionController(ISubscriptionService subscriptionService) : ControllerBase
{
    [HttpGet("plans")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<SubscriptionPlanResponse>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<SubscriptionPlanResponse>> GetPlans() =>
        Ok(subscriptionService.GetPlans());

    [HttpGet("me")]
    [Authorize(Roles = "User,Admin")]
    [ProducesResponseType<SubscriptionMeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<SubscriptionMeResponse>> GetMeAsync(
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subject, out var userId) || userId == Guid.Empty)
        {
            return Unauthorized(CreateProblem(
                StatusCodes.Status401Unauthorized,
                "Authentication identity is invalid.",
                "invalid_identity"));
        }

        var response = await subscriptionService.GetMySubscriptionAsync(userId, cancellationToken);
        if (response is null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, CreateProblem(
                StatusCodes.Status403Forbidden,
                "A persisted user account is required for subscription access.",
                "persisted_account_required"));
        }

        return Ok(response);
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
