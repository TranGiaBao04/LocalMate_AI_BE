using LocalMateAI.Application.DTOs.PlaceReviews;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/trips/items/{itemId:guid}/review")]
[Authorize(Roles = "User,Admin")]
public sealed class ItemReviewsController(IPlaceReviewService placeReviewService) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<PlaceReviewResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<PlaceReviewResponse>> CreateAsync(
        Guid itemId,
        [FromBody] CreatePlaceReviewRequest request,
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

        var result = await placeReviewService.CreateAsync(userId, itemId, request, cancellationToken);
        return result.Status switch
        {
            CreatePlaceReviewResultStatus.Success =>
                StatusCode(StatusCodes.Status201Created, result.Response),
            CreatePlaceReviewResultStatus.InvalidItemId =>
                BadRequest(CreateProblem(
                    StatusCodes.Status400BadRequest,
                    "Itinerary item ID is invalid.",
                    "invalid_itinerary_item_id")),
            CreatePlaceReviewResultStatus.ValidationFailed =>
                BadRequest(CreateValidationProblem(result.ValidationErrors!)),
            CreatePlaceReviewResultStatus.UserNotFound =>
                StatusCode(StatusCodes.Status403Forbidden, CreateProblem(
                    StatusCodes.Status403Forbidden,
                    "A persisted user account is required to review a place.",
                    "review_requires_persisted_user")),
            CreatePlaceReviewResultStatus.ItemNotFound =>
                NotFound(CreateProblem(
                    StatusCodes.Status404NotFound,
                    "Itinerary item was not found.",
                    "itinerary_item_not_found")),
            CreatePlaceReviewResultStatus.ItemNotVisited =>
                Conflict(CreateProblem(
                    StatusCodes.Status409Conflict,
                    "Itinerary item must be marked visited before it can be reviewed.",
                    "item_not_visited")),
            CreatePlaceReviewResultStatus.AlreadyExists =>
                Conflict(CreateProblem(
                    StatusCodes.Status409Conflict,
                    "A review already exists for this itinerary item.",
                    "review_already_exists")),
            _ => throw new InvalidOperationException("Unsupported place review result status.")
        };
    }

    [HttpGet]
    [ProducesResponseType<PlaceReviewResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlaceReviewResponse>> GetAsync(
        Guid itemId,
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

        var result = await placeReviewService.GetAsync(userId, itemId, cancellationToken);
        return result.Status switch
        {
            GetPlaceReviewResultStatus.Success => Ok(result.Response),
            GetPlaceReviewResultStatus.InvalidItemId =>
                BadRequest(CreateProblem(
                    StatusCodes.Status400BadRequest,
                    "Itinerary item ID is invalid.",
                    "invalid_itinerary_item_id")),
            GetPlaceReviewResultStatus.UserNotFound =>
                StatusCode(StatusCodes.Status403Forbidden, CreateProblem(
                    StatusCodes.Status403Forbidden,
                    "A persisted user account is required to view a review.",
                    "review_requires_persisted_user")),
            GetPlaceReviewResultStatus.ItemNotFound =>
                NotFound(CreateProblem(
                    StatusCodes.Status404NotFound,
                    "Itinerary item was not found.",
                    "itinerary_item_not_found")),
            GetPlaceReviewResultStatus.ReviewNotFound =>
                NotFound(CreateProblem(
                    StatusCodes.Status404NotFound,
                    "Review was not found.",
                    "review_not_found")),
            _ => throw new InvalidOperationException("Unsupported place review result status.")
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

    private ValidationProblemDetails CreateValidationProblem(
        IReadOnlyDictionary<string, string[]> validationErrors)
    {
        var problem = new ValidationProblemDetails(
            validationErrors.ToDictionary(error => error.Key, error => error.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Place review data is invalid.",
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "invalid_place_review";
        return problem;
    }
}
