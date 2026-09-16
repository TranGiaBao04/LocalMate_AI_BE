using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/trips")]
public sealed class TripsController(
    ITripFeasibilityService tripFeasibilityService,
    ITripService tripService) : ControllerBase
{
    [HttpPost("feasibility-check")]
    [AllowAnonymous]
    [ProducesResponseType<TripFeasibilityResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<TripFeasibilityResponse>> CheckFeasibilityAsync(
        [FromBody] TripRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await tripFeasibilityService.CheckFeasibilityAsync(request, cancellationToken);

        return result.Status switch
        {
            TripFeasibilityResultStatus.Success => Ok(result.Response),
            TripFeasibilityResultStatus.ValidationFailed =>
                CreateValidationProblem(result.ValidationErrors!),
            _ => throw new InvalidOperationException("Unsupported trip feasibility result status.")
        };
    }

    [HttpPost("save")]
    [Authorize(Roles = "User,Admin")]
    [ProducesResponseType<SaveTripResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<SaveTripResponse>> SaveTripAsync(
        [FromBody] SaveTripRequest request,
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

        var result = await tripService.SaveTripAsync(userId, request, cancellationToken);
        return result.Status switch
        {
            SaveTripResultStatus.Success => Ok(result.Response),
            SaveTripResultStatus.InvalidTripId =>
                BadRequest(CreateProblem(
                    StatusCodes.Status400BadRequest,
                    "Trip ID is invalid.",
                    "invalid_trip_id")),
            SaveTripResultStatus.UserNotFound =>
                NotFound(CreateProblem(
                    StatusCodes.Status404NotFound,
                    "Current user was not found.",
                    "user_not_found")),
            SaveTripResultStatus.TripNotFound =>
                NotFound(CreateProblem(
                    StatusCodes.Status404NotFound,
                    "Trip was not found.",
                    "trip_not_found")),
            _ => throw new InvalidOperationException("Unsupported save trip result status.")
        };
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
}
