using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.Subscription;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/admin/trial-programs")]
[HasPermission(Permissions.ManagePlans)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AdminTrialProgramsController(ITrialProgramService trialProgramService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TrialProgramResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAllAsync(CancellationToken cancellationToken) =>
        Ok(await trialProgramService.GetAllAsync(cancellationToken));

    [HttpPost]
    [ProducesResponseType<TrialProgramResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] SaveTrialProgramRequest request,
        CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst(AuthClaimNames.Subject)?.Value, out var actorUserId)
            || actorUserId == Guid.Empty)
        {
            return Unauthorized(CreateProblem(StatusCodes.Status401Unauthorized,
                "Authentication identity is invalid.", "invalid_identity"));
        }

        var result = await trialProgramService.CreateAsync(request, actorUserId, cancellationToken);
        return result.Status == TrialProgramResultStatus.Success
            ? StatusCode(StatusCodes.Status201Created, result.Response)
            : ToErrorResult(result);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<TrialProgramResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] SaveTrialProgramRequest request,
        CancellationToken cancellationToken)
    {
        var result = await trialProgramService.UpdateAsync(id, request, cancellationToken);
        return result.Status == TrialProgramResultStatus.Success ? Ok(result.Response) : ToErrorResult(result);
    }

    private IActionResult ToErrorResult(TrialProgramResult result) => result.Status switch
    {
        TrialProgramResultStatus.ValidationFailed => BadRequest(CreateInvalidProgramProblem(result.ValidationErrors!)),
        TrialProgramResultStatus.NotFound => NotFound(CreateProblem(StatusCodes.Status404NotFound,
            "Trial program not found.", "trial_program_not_found")),
        TrialProgramResultStatus.Overlap => Conflict(CreateProblem(StatusCodes.Status409Conflict,
            "Trial program overlaps another enabled program.", "trial_program_overlap",
            "Khoảng ngày trùng với một chương trình dùng thử đang bật khác.")),
        _ => throw new InvalidOperationException("Unknown trial program result.")
    };

    private ValidationProblemDetails CreateInvalidProgramProblem(IReadOnlyDictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors.ToDictionary(error => error.Key, error => error.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Trial program is invalid.",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "invalid_trial_program";
        return problem;
    }

    private ProblemDetails CreateProblem(int status, string title, string code, string? detail = null)
    {
        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Instance = HttpContext.Request.Path };
        problem.Extensions["code"] = code;
        return problem;
    }
}
