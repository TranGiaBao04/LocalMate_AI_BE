using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/trips")]
[AllowAnonymous]
public sealed class TripsController(ITripFeasibilityService tripFeasibilityService) : ControllerBase
{
    [HttpPost("feasibility-check")]
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
