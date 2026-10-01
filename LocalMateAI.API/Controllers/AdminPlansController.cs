using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Plans;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/admin/plans")]
[HasPermission(Permissions.ManagePlans)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AdminPlansController(IAdminPlanService service) : ControllerBase
{
    private const string DetailRoute = "GetAdminPlan";

    [HttpGet]
    [ProducesResponseType<PagedResult<AdminPlanResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListAsync([FromQuery] AdminPlanQuery query, CancellationToken cancellationToken)
    {
        var result = await service.GetPlansAsync(query, cancellationToken);
        return result.Status == AdminPlanResultStatus.Success ? Ok(result.Response)
            : Failure(result.Status, result.ValidationErrors);
    }

    [HttpGet("features")]
    [ProducesResponseType<IReadOnlyList<AdminPlanFeatureResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> FeaturesAsync(CancellationToken cancellationToken) =>
        Ok(await service.GetFeaturesAsync(cancellationToken));

    [HttpGet("{id:guid}", Name = DetailRoute)]
    [ProducesResponseType<AdminPlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var response = await service.GetPlanAsync(id, cancellationToken);
        return response is null ? Failure(AdminPlanResultStatus.NotFound) : Ok(response);
    }

    [HttpPost]
    [ProducesResponseType<AdminPlanResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync([FromBody] CreateAdminPlanRequest request, CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);
        return result.Status == AdminPlanResultStatus.Success
            ? CreatedAtRoute(DetailRoute, new { id = result.Response!.Id }, result.Response)
            : Failure(result.Status, result.ValidationErrors);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<AdminPlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(Guid id, [FromBody] UpdateAdminPlanRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, request, cancellationToken);
        return result.Status == AdminPlanResultStatus.Success ? Ok(result.Response) : Failure(result.Status, result.ValidationErrors);
    }

    [HttpPut("{id:guid}/status")]
    [ProducesResponseType<AdminPlanResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> StatusAsync(Guid id, [FromBody] AdminPlanStatusRequest request, CancellationToken cancellationToken)
    {
        var result = await service.SetStatusAsync(id, request.IsActive, cancellationToken);
        return result.Status == AdminPlanResultStatus.Success ? Ok(result.Response) : Failure(result.Status, result.ValidationErrors);
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken);
        return result.Status == AdminPlanResultStatus.Success ? NoContent() : Failure(result.Status, result.ValidationErrors);
    }

    [HttpGet("{id:guid}/versions")]
    [ProducesResponseType<PagedResult<AdminPlanVersionResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> VersionsAsync(Guid id, [FromQuery] AdminPlanVersionsQuery query, CancellationToken cancellationToken)
    {
        var result = await service.GetVersionsAsync(id, query, cancellationToken);
        return result.Status == AdminPlanResultStatus.Success ? Ok(result.Response) : Failure(result.Status, result.ValidationErrors);
    }

    private IActionResult Failure(AdminPlanResultStatus status, IReadOnlyDictionary<string, string[]>? errors = null)
    {
        if (status == AdminPlanResultStatus.ValidationFailed)
        {
            var validation = new ValidationProblemDetails(errors!.ToDictionary(e => e.Key, e => e.Value))
            { Status = 400, Title = "Plan data is invalid.", Instance = Request.Path };
            validation.Extensions["code"] = "invalid_plan_data";
            return BadRequest(validation);
        }
        var (http, code, title) = status switch
        {
            AdminPlanResultStatus.NotFound => (404, "plan_not_found", "Plan was not found."),
            AdminPlanResultStatus.CodeExists => (409, "plan_code_exists", "Plan code already exists."),
            AdminPlanResultStatus.PriorityExists => (409, "plan_priority_exists", "Plan priority already exists."),
            AdminPlanResultStatus.SystemPlanLocked => (409, "system_plan_locked", "System plan cannot be deleted."),
            AdminPlanResultStatus.PlanInUse => (409, "plan_in_use", "Plan has published history or references."),
            AdminPlanResultStatus.FreeCannotDeactivate => (409, "free_cannot_deactivate", "Free cannot be deactivated."),
            AdminPlanResultStatus.InvalidCurrentVersion => (409, "invalid_current_plan_version", "Plan has no valid current version."),
            _ => throw new InvalidOperationException("Unknown admin plan result.")
        };
        var problem = new ProblemDetails { Status = http, Title = title, Type = $"https://httpstatuses.com/{http}", Instance = Request.Path };
        problem.Extensions["code"] = code;
        return StatusCode(http, problem);
    }
}
