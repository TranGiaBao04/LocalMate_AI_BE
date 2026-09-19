using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/admin/places")]
[Authorize(Roles = "Admin")]
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
    [ProducesResponseType<IReadOnlyList<AdminPlaceResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AdminPlaceResponse>>> GetAllAsync(
        CancellationToken cancellationToken)
    {
        var response = await adminPlaceService.GetAllAsync(cancellationToken);
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
            _ => throw new InvalidOperationException("Unknown Admin Place update result.")
        };
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await adminPlaceService.DeleteAsync(id, cancellationToken);

        return result.Status switch
        {
            DeleteAdminPlaceResultStatus.Success => NoContent(),
            DeleteAdminPlaceResultStatus.NotFound => NotFound(CreatePlaceNotFoundProblem()),
            DeleteAdminPlaceResultStatus.InUse => Conflict(CreatePlaceInUseProblem()),
            _ => throw new InvalidOperationException("Unknown Admin Place delete result.")
        };
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

    private ProblemDetails CreatePlaceInUseProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Place is referenced by itinerary history.",
            Type = "https://httpstatuses.com/409",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "place_in_use";
        return problem;
    }
}
