using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.AdminAiUsage;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Dashboard;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/admin")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AdminAiUsageController(IAdminAiUsageService service) : ControllerBase
{
    [HttpGet("ai-usage/summary")]
    [HasPermission(Permissions.ViewRevenue)]
    [ProducesResponseType<AdminAiUsageSystemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetSummaryAsync([FromQuery] DashboardDateRangeQuery query, CancellationToken cancellationToken) =>
        Map(await service.GetSummaryAsync(query, cancellationToken));

    [HttpGet("users/{userId:guid}/ai-usage")]
    [HasPermission(Permissions.ManageUsers)]
    [ProducesResponseType<AdminAiUsageUserResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserSummaryAsync(Guid userId, [FromQuery] DashboardDateRangeQuery query,
        CancellationToken cancellationToken) => Map(await service.GetUserSummaryAsync(userId, query, cancellationToken));

    [HttpGet("users/{userId:guid}/ai-calls")]
    [AdminAiUsageModelState]
    [HasPermission(Permissions.ManageUsers)]
    [ProducesResponseType<PagedResult<AdminAiCallReadModel>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetUserCallsAsync(Guid userId, [FromQuery] AdminAiCallsRequest query,
        CancellationToken cancellationToken) => Map(await service.GetUserCallsAsync(userId, query, cancellationToken));

    private IActionResult Map<T>(AdminAiUsageResult<T> result) => result.Status switch
    {
        AdminAiUsageResultStatus.Success => Ok(result.Response),
        AdminAiUsageResultStatus.InvalidQuery => BadRequest(InvalidQueryProblem(Request.Path,
            result.ValidationErrors!.ToDictionary(pair => pair.Key, pair => pair.Value))),
        AdminAiUsageResultStatus.UserNotFound => NotFound(UserNotFoundProblem()),
        _ => throw new InvalidOperationException("Unsupported AI usage service result.")
    };

    internal static ValidationProblemDetails InvalidQueryProblem(string path, IDictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest, Title = "AI usage query is invalid.", Instance = path
        };
        problem.Extensions["code"] = "invalid_ai_usage_query";
        return problem;
    }

    private ProblemDetails UserNotFoundProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound, Title = "User was not found.", Instance = Request.Path
        };
        problem.Extensions["code"] = "user_not_found";
        return problem;
    }
}

// Runs before ApiController's automatic model-state filter (-2000), on this action only.
internal sealed class AdminAiUsageModelStateAttribute : ActionFilterAttribute
{
    public AdminAiUsageModelStateAttribute() { Order = -2001; }

    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (context.ModelState.IsValid) return;
        var fields = new[] { "From", "To", "Page", "PageSize", "SortBy", "SortDirection", "Kind", "Status" };
        var errors = context.ModelState.Where(entry => entry.Value!.Errors.Count > 0)
            .GroupBy(entry => fields.FirstOrDefault(field => string.Equals(field, entry.Key.Split('.').Last(),
                StringComparison.OrdinalIgnoreCase)) ?? entry.Key)
            .ToDictionary(group => group.Key, _ => new[] { "Query value is invalid." });
        context.Result = new BadRequestObjectResult(AdminAiUsageController.InvalidQueryProblem(context.HttpContext.Request.Path, errors));
    }
}
