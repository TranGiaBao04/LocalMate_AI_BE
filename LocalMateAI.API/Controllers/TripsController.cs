using LocalMateAI.Application.DTOs.Matching;
using LocalMateAI.Application.DTOs.Trips;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/trips")]
public sealed class TripsController(
    ITripFeasibilityService tripFeasibilityService,
    ITripMatchingService tripMatchingService,
    IHeuristicFallbackEngine heuristicFallbackEngine,
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

    [HttpPost("match")]
    [Authorize(Roles = "User,Admin")]
    [ProducesResponseType<TripMatchingResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<TripMatchingResponse>> MatchAsync(
        [FromBody] TripRequestDto request,
        CancellationToken cancellationToken)
    {
        var result = await tripMatchingService.MatchAsync(request, cancellationToken);

        return result.Status switch
        {
            TripMatchingResultStatus.Success => Ok(result.Response),
            TripMatchingResultStatus.ValidationFailed =>
                CreateValidationProblem(result.ValidationErrors!),
            _ => throw new InvalidOperationException("Unsupported trip matching result status.")
        };
    }

    [HttpPost("fallback-itinerary")]
    [Authorize(Roles = "User,Admin")]
    [ProducesResponseType<FallbackItineraryPayload>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<FallbackItineraryPayload>> GenerateFallbackItineraryAsync(
        [FromBody] TripRequestDto request,
        [FromQuery] string reason = "heuristic",
        CancellationToken cancellationToken = default)
    {
        var result = await heuristicFallbackEngine.GenerateFallbackAsync(
            request,
            reason,
            cancellationToken);

        return result.Status switch
        {
            FallbackItineraryStatus.Success => Ok(result.Payload),
            FallbackItineraryStatus.ValidationFailed =>
                CreateValidationProblem(result.ValidationErrors!),
            _ => throw new InvalidOperationException("Unsupported fallback itinerary result status.")
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

    [HttpPost("{tripId:guid}/finalize")]
    [Authorize(Roles = "User,Admin")]
    [ProducesResponseType<FinalizeTripResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FinalizeTripResponse>> FinalizeTripAsync(
        Guid tripId,
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

        var result = await tripService.FinalizeTripAsync(userId, tripId, cancellationToken);
        return result.Status switch
        {
            FinalizeTripResultStatus.Success => Ok(result.Response),
            FinalizeTripResultStatus.InvalidTripId =>
                BadRequest(CreateProblem(
                    StatusCodes.Status400BadRequest,
                    "Trip ID is invalid.",
                    "invalid_trip_id")),
            FinalizeTripResultStatus.TripNotFound =>
                NotFound(CreateProblem(
                    StatusCodes.Status404NotFound,
                    "Trip was not found.",
                    "trip_not_found")),
            FinalizeTripResultStatus.AlreadyFinalized =>
                Conflict(CreateProblem(
                    StatusCodes.Status409Conflict,
                    "Trip is already finalized.",
                    "already_finalized")),
            _ => throw new InvalidOperationException("Unsupported finalize trip result status.")
        };
    }

    [HttpGet("my-trips")]
    [Authorize(Roles = "User,Admin")]
    [ProducesResponseType<IReadOnlyList<MyTripResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<MyTripResponse>>> GetMyTripsAsync(
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

        var result = await tripService.GetMyTripsAsync(userId, cancellationToken);
        return result.UserFound
            ? Ok(result.Trips)
            : NotFound(CreateProblem(
                StatusCodes.Status404NotFound,
                "Current user was not found.",
                "user_not_found"));
    }

    [HttpPut("items/{id:guid}/visit")]
    [Authorize(Roles = "User,Admin")]
    [ProducesResponseType<VisitItineraryItemResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<VisitItineraryItemResponse>> MarkItineraryItemVisitedAsync(
        Guid id,
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

        var result = await tripService.MarkItineraryItemVisitedAsync(userId, id, cancellationToken);
        return result.Status switch
        {
            VisitItineraryItemResultStatus.Success => Ok(result.Response),
            VisitItineraryItemResultStatus.InvalidItemId =>
                BadRequest(CreateProblem(
                    StatusCodes.Status400BadRequest,
                    "Itinerary item ID is invalid.",
                    "invalid_itinerary_item_id")),
            VisitItineraryItemResultStatus.UserNotFound =>
                StatusCode(StatusCodes.Status403Forbidden, CreateProblem(
                    StatusCodes.Status403Forbidden,
                    "A persisted user account is required to mark a visit.",
                    "visit_requires_persisted_user")),
            VisitItineraryItemResultStatus.ItemNotFound =>
                NotFound(CreateProblem(
                    StatusCodes.Status404NotFound,
                    "Itinerary item was not found.",
                    "itinerary_item_not_found")),
            VisitItineraryItemResultStatus.TripNotFinalized =>
                Conflict(CreateProblem(
                    StatusCodes.Status409Conflict,
                    "Trip must be finalized before an item can be marked visited.",
                    "trip_not_finalized")),
            _ => throw new InvalidOperationException("Unsupported visit itinerary item result status.")
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
