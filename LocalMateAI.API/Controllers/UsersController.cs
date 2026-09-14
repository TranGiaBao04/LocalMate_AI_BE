using LocalMateAI.Application.DTOs.Users;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/users")]
public sealed class UsersController(IUserService userService) : ControllerBase
{
    [HttpGet("me")]
    [Authorize(Roles = "User,Admin")]
    [ProducesResponseType<UserProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileResponse>> GetCurrentUserAsync(
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subject, out var userId) || userId == Guid.Empty)
        {
            return Unauthorized(CreateInvalidIdentityProblem());
        }

        var profile = await userService.GetCurrentUserAsync(userId, cancellationToken);
        if (profile is null)
        {
            return NotFound(CreateUserNotFoundProblem());
        }

        return Ok(profile);
    }

    [HttpPatch("me")]
    [Authorize(Roles = "User,Admin")]
    [ProducesResponseType<UserProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserProfileResponse>> UpdateCurrentUserAsync(
        [FromBody] UpdateCurrentUserRequest request,
        CancellationToken cancellationToken)
    {
        var subject = User.FindFirst("sub")?.Value;
        if (!Guid.TryParse(subject, out var userId) || userId == Guid.Empty)
        {
            return Unauthorized(CreateInvalidIdentityProblem());
        }

        var result = await userService.UpdateCurrentUserAsync(
            userId,
            request,
            cancellationToken);

        return result.Status switch
        {
            UpdateCurrentUserResultStatus.Success => Ok(result.Response),
            UpdateCurrentUserResultStatus.ValidationFailed =>
                CreateValidationProblem(result.ValidationErrors!),
            UpdateCurrentUserResultStatus.NoChangesRequest =>
                BadRequest(CreateNoChangesProblem()),
            UpdateCurrentUserResultStatus.InvalidPreference =>
                BadRequest(CreateInvalidPreferenceProblem()),
            UpdateCurrentUserResultStatus.UserNotFound =>
                NotFound(CreateUserNotFoundProblem()),
            _ => throw new InvalidOperationException("Unsupported user update result status.")
        };
    }

    private ObjectResult CreateValidationProblem(
        IReadOnlyDictionary<string, string[]> validationErrors)
    {
        var problem = new ValidationProblemDetails(
            validationErrors.ToDictionary(error => error.Key, error => error.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };

        var result = new BadRequestObjectResult(problem);
        result.ContentTypes.Add("application/problem+json");
        return result;
    }

    private ProblemDetails CreateNoChangesProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "No profile changes were supplied.",
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "no_changes";
        return problem;
    }

    private ProblemDetails CreateInvalidPreferenceProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more preferences are invalid.",
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "invalid_preference";
        return problem;
    }

    private ProblemDetails CreateInvalidIdentityProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status401Unauthorized,
            Title = "Authentication identity is invalid.",
            Type = "https://httpstatuses.com/401",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "invalid_identity";
        return problem;
    }

    private ProblemDetails CreateUserNotFoundProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Current user profile was not found.",
            Type = "https://httpstatuses.com/404",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "user_not_found";
        return problem;
    }
}
