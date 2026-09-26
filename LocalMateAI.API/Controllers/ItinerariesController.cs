using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/itineraries")]
public sealed class ItinerariesController(
    ICuratedItineraryService curatedItineraryService,
    ICuratedTripService curatedTripService) : ControllerBase
{
    [HttpGet("curated")]
    [ProducesResponseType<IReadOnlyList<CuratedItineraryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CuratedItineraryResponse>>> GetCurated(CancellationToken cancellationToken)
    {
        var result = await curatedItineraryService.GetCuratedItinerariesAsync(cancellationToken);

        return Ok(result);
    }

    [HttpPost("curated/{curatedId:guid}/apply")]
    [Authorize(Roles = "User,Admin")]
    [ProducesResponseType<TripDetailResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<TripDetailResponse>> ApplyCuratedItineraryAsync(
        Guid curatedId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] ApplyCuratedItineraryRequest? request,
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

        var result = await curatedTripService.ApplyAsync(userId, curatedId, request, cancellationToken);
        return result.Status switch
        {
            ApplyCuratedItineraryResultStatus.Success =>
                StatusCode(StatusCodes.Status201Created, result.Response),
            ApplyCuratedItineraryResultStatus.InvalidId =>
                BadRequest(CreateProblem(
                    StatusCodes.Status400BadRequest,
                    "Curated itinerary ID is invalid.",
                    "invalid_id")),
            ApplyCuratedItineraryResultStatus.InvalidStart =>
                BadRequest(CreateProblem(
                    StatusCodes.Status400BadRequest,
                    "Start location must include both latitude and longitude inside Ho Chi Minh City.",
                    "invalid_start_location")),
            ApplyCuratedItineraryResultStatus.ValidationFailed =>
                CreateValidationProblem(result.ValidationErrors!),
            ApplyCuratedItineraryResultStatus.OutOfServiceArea =>
                Conflict(CreateProblem(
                    StatusCodes.Status409Conflict,
                    "The start location is outside the Metro Line 1 service area.",
                    "out_of_service_area")),
            ApplyCuratedItineraryResultStatus.UserNotFound =>
                StatusCode(StatusCodes.Status403Forbidden, CreateProblem(
                    StatusCodes.Status403Forbidden,
                    "A persisted user account is required to apply a curated itinerary.",
                    "apply_requires_persisted_user")),
            ApplyCuratedItineraryResultStatus.ItineraryNotFound =>
                NotFound(CreateProblem(
                    StatusCodes.Status404NotFound,
                    "Curated itinerary was not found.",
                    "curated_itinerary_not_found")),
            ApplyCuratedItineraryResultStatus.ItineraryUnavailable =>
                Conflict(CreateProblem(
                    StatusCodes.Status409Conflict,
                    "Curated itinerary has no available places.",
                    "curated_itinerary_unavailable")),
            _ => throw new InvalidOperationException("Unsupported apply curated itinerary result status.")
        };
    }

    private ObjectResult CreateValidationProblem(IReadOnlyDictionary<string, string[]> validationErrors)
    {
        var problem = new ValidationProblemDetails(
            validationErrors.ToDictionary(error => error.Key, error => error.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "One or more validation errors occurred.",
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };

        return BadRequest(problem);
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
