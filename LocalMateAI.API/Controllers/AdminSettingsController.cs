using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.Settings;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/admin/settings")]
[HasPermission(Permissions.ManageSettings)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AdminSettingsController(IAdminSystemSettingService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SystemSettingResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<SystemSettingResponse>>> GetAllAsync(
        CancellationToken cancellationToken) =>
        Ok(await service.GetAllAsync(cancellationToken));

    [HttpPut("{key}")]
    [ProducesResponseType<SystemSettingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(string key, [FromBody] UpdateSystemSettingRequest request,
        CancellationToken cancellationToken)
    {
        if (!TryGetActorId(out var actorUserId))
        {
            return Unauthorized(Problem(StatusCodes.Status401Unauthorized, "Authentication identity is invalid.",
                "invalid_identity"));
        }

        return ToActionResult(await service.UpdateAsync(key, request, actorUserId, cancellationToken));
    }

    [HttpDelete("{key}")]
    [ProducesResponseType<SystemSettingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ResetAsync(string key, CancellationToken cancellationToken) =>
        ToActionResult(await service.ResetAsync(key, cancellationToken));

    private IActionResult ToActionResult(AdminSystemSettingResult result) => result.Status switch
    {
        AdminSystemSettingResultStatus.Success => Ok(result.Response),
        AdminSystemSettingResultStatus.NotFound => NotFound(Problem(StatusCodes.Status404NotFound,
            "System setting was not found.", "setting_not_found")),
        _ => BadRequest(InvalidValueProblem(result.Error!))
    };

    private bool TryGetActorId(out Guid actorUserId) =>
        Guid.TryParse(User.FindFirst(AuthClaimNames.Subject)?.Value, out actorUserId) && actorUserId != Guid.Empty;

    private ValidationProblemDetails InvalidValueProblem(string error)
    {
        var problem = new ValidationProblemDetails(new Dictionary<string, string[]> { ["Value"] = [error] })
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "System setting value is invalid.",
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };
        problem.Extensions["code"] = "invalid_setting_value";
        return problem;
    }

    private ProblemDetails Problem(int status, string title, string code)
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
