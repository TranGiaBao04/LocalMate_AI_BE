using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/admin/places/{placeId:guid}/opening-hours")]
[HasPermission(Permissions.ManagePlaces)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AdminPlaceOpeningHoursController(IPlaceOpeningHourService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<PlaceOpeningHoursResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(Guid placeId, CancellationToken cancellationToken)
    {
        var response = await service.GetAsync(placeId, cancellationToken);
        return response is null ? NotFound(NotFoundProblem()) : Ok(response);
    }

    [HttpPut]
    [ProducesResponseType<PlaceOpeningHoursResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SaveAsync(
        Guid placeId,
        [FromBody] SavePlaceOpeningHoursRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.SaveAsync(placeId, request, cancellationToken);

        return result.Status switch
        {
            SavePlaceOpeningHoursResultStatus.Success => Ok(result.Response),
            SavePlaceOpeningHoursResultStatus.ValidationFailed =>
                BadRequest(InvalidProblem(result.ValidationErrors!)),
            SavePlaceOpeningHoursResultStatus.NotFound => NotFound(NotFoundProblem()),
            _ => throw new InvalidOperationException("Unknown opening hours save result.")
        };
    }

    private ValidationProblemDetails InvalidProblem(IReadOnlyDictionary<string, string[]> errors)
    {
        var problem = new ValidationProblemDetails(errors.ToDictionary(error => error.Key, error => error.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Opening hours data is invalid.",
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "invalid_opening_hours";
        return problem;
    }

    private ProblemDetails NotFoundProblem()
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
}
