using LocalMateAI.Application.DTOs.Feedback;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/feedback")]
public sealed class FeedbackController(IFeedbackService feedbackService) : ControllerBase
{
    [HttpPost("trip")]
    [Authorize(Roles = "User,Admin")]
    [ProducesResponseType<FeedbackResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<FeedbackResponse>> CreateTripFeedbackAsync(
        [FromBody] CreateFeedbackRequest request,
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

        var result = await feedbackService.CreateAsync(userId, request, cancellationToken);

        return result.Status switch
        {
            CreateFeedbackResultStatus.Success =>
                StatusCode(StatusCodes.Status201Created, result.Response),
            CreateFeedbackResultStatus.ValidationFailed =>
                BadRequest(CreateValidationProblem(result.ValidationErrors!)),
            CreateFeedbackResultStatus.NonPersistedUser =>
                StatusCode(StatusCodes.Status403Forbidden, CreateProblem(
                    StatusCodes.Status403Forbidden,
                    "A persisted user account is required to create feedback.",
                    "feedback_requires_persisted_user")),
            CreateFeedbackResultStatus.TripNotFound =>
                NotFound(CreateProblem(
                    StatusCodes.Status404NotFound,
                    "Trip was not found.",
                    "trip_not_found")),
            CreateFeedbackResultStatus.TripNotFinalized =>
                Conflict(CreateProblem(
                    StatusCodes.Status409Conflict,
                    "Trip must be finalized before feedback can be created.",
                    "trip_not_finalized")),
            CreateFeedbackResultStatus.AlreadyExists =>
                Conflict(CreateProblem(
                    StatusCodes.Status409Conflict,
                    "Feedback already exists for this trip.",
                    "feedback_already_exists")),
            _ => throw new InvalidOperationException("Unsupported feedback result status.")
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
            Title = "Feedback data is invalid.",
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "invalid_feedback";
        return problem;
    }
}
