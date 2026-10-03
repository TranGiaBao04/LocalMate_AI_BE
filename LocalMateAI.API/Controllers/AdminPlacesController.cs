using FluentValidation;
using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/admin/places")]
[HasPermission(Permissions.ManagePlaces)]
public sealed class AdminPlacesController(IAdminPlaceService adminPlaceService) : ControllerBase
{
    private const string GetByIdRouteName = "GetAdminPlaceById";

    [HttpPost]
    [ProducesResponseType<AdminPlaceResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminPlaceResponse>> CreateAsync(
        [FromBody] CreateAdminPlaceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await adminPlaceService.CreateAsync(request, cancellationToken);

        return result.Status switch
        {
            AdminPlaceOperationResultStatus.Success => CreatedAtRoute(
                GetByIdRouteName,
                new { id = result.Response!.Id },
                result.Response),
            AdminPlaceOperationResultStatus.ValidationFailed =>
                BadRequest(CreateInvalidPlaceProblem(result.ValidationErrors!)),
            _ => throw new InvalidOperationException("Unknown Admin Place create result.")
        };
    }

    [HttpGet]
    [ProducesResponseType<PagedResult<AdminPlaceResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<AdminPlaceResponse>>> GetPagedAsync(
        [FromQuery] AdminPlaceQuery query,
        [FromServices] IValidator<AdminPlaceQuery> queryValidator,
        CancellationToken cancellationToken)
    {
        var validationResult = await queryValidator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BadRequest(CreateInvalidPlaceProblem(
                validationResult.ToDictionary().ToDictionary(k => k.Key, v => v.Value)));
        }

        var response = await adminPlaceService.GetPagedAsync(query, cancellationToken);
        return Ok(response);
    }

    [HttpGet("{id:guid}", Name = GetByIdRouteName)]
    [ProducesResponseType<AdminPlaceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AdminPlaceResponse>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var response = await adminPlaceService.GetByIdAsync(id, cancellationToken);

        return response is null
            ? NotFound(CreatePlaceNotFoundProblem())
            : Ok(response);
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<AdminPlaceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminPlaceResponse>> UpdateAsync(
        Guid id,
        [FromBody] UpdateAdminPlaceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await adminPlaceService.UpdateAsync(id, request, cancellationToken);

        return result.Status switch
        {
            AdminPlaceOperationResultStatus.Success => Ok(result.Response),
            AdminPlaceOperationResultStatus.ValidationFailed =>
                BadRequest(CreateInvalidPlaceProblem(result.ValidationErrors!)),
            AdminPlaceOperationResultStatus.NotFound =>
                NotFound(CreatePlaceNotFoundProblem()),
            AdminPlaceOperationResultStatus.ConcurrencyConflict =>
                Conflict(CreateProblem(
                    StatusCodes.Status409Conflict,
                    "Place data was modified by another request. Please refresh and try again.",
                    "concurrency_conflict")),
            _ => throw new InvalidOperationException("Unknown Admin Place update result.")
        };
    }

    [HttpPut("{id:guid}/status")]
    [ProducesResponseType<AdminPlaceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminPlaceResponse>> UpdateStatusAsync(
        Guid id,
        [FromBody] UpdatePlaceStatusRequest request,
        CancellationToken cancellationToken)
    {
        var result = await adminPlaceService.UpdateStatusAsync(id, request, cancellationToken);

        return result.Status switch
        {
            AdminPlaceModerationResultStatus.Success => Ok(result.Response),
            AdminPlaceModerationResultStatus.InvalidStatus =>
                BadRequest(CreateProblem(
                    StatusCodes.Status400BadRequest,
                    "Place status is invalid.",
                    "invalid_place_status")),
            AdminPlaceModerationResultStatus.NotFound =>
                NotFound(CreatePlaceNotFoundProblem()),
            AdminPlaceModerationResultStatus.InvalidStatusTransition =>
                Conflict(CreateProblem(
                    StatusCodes.Status409Conflict,
                    "Place status transition is not allowed.",
                    "invalid_place_status_transition")),
            AdminPlaceModerationResultStatus.ConcurrencyConflict =>
                Conflict(CreateProblem(
                    StatusCodes.Status409Conflict,
                    "Place data was modified by another request. Please refresh and try again.",
                    "concurrency_conflict")),
            _ => throw new InvalidOperationException("Unknown Admin Place status result.")
        };
    }

    [HttpPut("{id:guid}/verification")]
    [ProducesResponseType<AdminPlaceResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AdminPlaceResponse>> UpdateVerificationAsync(
        Guid id,
        [FromBody] UpdatePlaceVerificationRequest request,
        CancellationToken cancellationToken)
    {
        var result = await adminPlaceService.UpdateVerificationAsync(id, request, cancellationToken);

        return result.Status switch
        {
            AdminPlaceModerationResultStatus.Success => Ok(result.Response),
            AdminPlaceModerationResultStatus.NotFound =>
                NotFound(CreatePlaceNotFoundProblem()),
            AdminPlaceModerationResultStatus.VerificationRequiresActive =>
                Conflict(CreateProblem(
                    StatusCodes.Status409Conflict,
                    "Place must be Active before verification can be enabled.",
                    "verification_requires_active")),
            AdminPlaceModerationResultStatus.ConcurrencyConflict =>
                Conflict(CreateProblem(
                    StatusCodes.Status409Conflict,
                    "Place data was modified by another request. Please refresh and try again.",
                    "concurrency_conflict")),
            _ => throw new InvalidOperationException("Unknown Admin Place verification result.")
        };
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await adminPlaceService.DeleteAsync(id, cancellationToken);

        return result.Status switch
        {
            DeleteAdminPlaceResultStatus.Success => NoContent(),
            DeleteAdminPlaceResultStatus.NotFound => NotFound(CreatePlaceNotFoundProblem()),
            _ => throw new InvalidOperationException("Unknown Admin Place delete result.")
        };
    }

    [HttpPost("validate-distance")]
    [ProducesResponseType<PlaceDistanceValidationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlaceDistanceValidationResult>> ValidateDistanceAsync(
        [FromBody] ValidatePlaceDistanceRequest request,
        [FromServices] IValidator<ValidatePlaceDistanceRequest> validator,
        [FromServices] IPlaceDistanceValidationService distanceValidationService,
        CancellationToken cancellationToken)
    {
        var validationResult = await validator.ValidateAsync(request, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BadRequest(CreateInvalidPlaceProblem(
                validationResult.ToDictionary().ToDictionary(k => k.Key, v => v.Value)));
        }

        var result = await distanceValidationService.ValidateDistanceAsync(request, cancellationToken);
        return result is null
            ? NotFound(CreateProblem(StatusCodes.Status404NotFound, "Station or coordinates was not found in Metro service area.", "station_not_found"))
            : Ok(result);
    }

    [HttpGet("{id:guid}/validate-distance")]
    [ProducesResponseType<PlaceDistanceValidationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlaceDistanceValidationResult>> ValidatePlaceDistanceAsync(
        Guid id,
        [FromQuery] Guid? stationId,
        [FromServices] IPlaceDistanceValidationService distanceValidationService,
        CancellationToken cancellationToken)
    {
        var result = await distanceValidationService.ValidatePlaceDistanceAsync(id, stationId, cancellationToken);
        return result is null
            ? NotFound(CreatePlaceNotFoundProblem())
            : Ok(result);
    }

    private ValidationProblemDetails CreateInvalidPlaceProblem(
        IReadOnlyDictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(
            errors.ToDictionary(error => error.Key, error => error.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Place data is invalid.",
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "invalid_place";
        return problem;
    }

    private ProblemDetails CreatePlaceNotFoundProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Place was not found.",
            Type = "https://httpstatuses.com/404",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "place_not_found";
        return problem;
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
