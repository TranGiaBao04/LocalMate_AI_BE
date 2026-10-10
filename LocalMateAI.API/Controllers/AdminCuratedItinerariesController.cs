using FluentValidation;
using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/admin/curated-itineraries")]
[HasPermission(Permissions.ManagePlaces)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AdminCuratedItinerariesController(IAdminCuratedItineraryService service) : ControllerBase
{
    private const string GetByIdRouteName = "GetAdminCuratedItineraryById";

    [HttpGet]
    [ProducesResponseType<PagedResult<AdminCuratedItineraryResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetPagedAsync(
        [FromQuery] AdminCuratedItineraryQuery query,
        [FromServices] IValidator<AdminCuratedItineraryQuery> queryValidator,
        CancellationToken cancellationToken)
    {
        var validationResult = await queryValidator.ValidateAsync(query, cancellationToken);
        if (!validationResult.IsValid)
        {
            return BadRequest(InvalidQueryProblem(validationResult.ToDictionary()));
        }

        return Ok(await service.GetPagedAsync(query, cancellationToken));
    }

    [HttpGet("{id:guid}", Name = GetByIdRouteName)]
    [ProducesResponseType<AdminCuratedItineraryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        var itinerary = await service.GetByIdAsync(id, cancellationToken);
        return itinerary is null ? NotFound(NotFoundProblem()) : Ok(itinerary);
    }

    [HttpPost]
    [ProducesResponseType<AdminCuratedItineraryResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CreateAsync(
        [FromBody] SaveCuratedItineraryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(request, cancellationToken);

        return result.Status switch
        {
            AdminCuratedItineraryResultStatus.Success => CreatedAtRoute(
                GetByIdRouteName, new { id = result.Response!.Id }, result.Response),
            AdminCuratedItineraryResultStatus.ValidationFailed =>
                BadRequest(InvalidItineraryProblem(result.ValidationErrors!)),
            _ => throw new InvalidOperationException("Unknown curated itinerary create result.")
        };
    }

    [HttpPut("{id:guid}")]
    [ProducesResponseType<AdminCuratedItineraryResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UpdateAsync(
        Guid id,
        [FromBody] SaveCuratedItineraryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, request, cancellationToken);

        return result.Status switch
        {
            AdminCuratedItineraryResultStatus.Success => Ok(result.Response),
            AdminCuratedItineraryResultStatus.ValidationFailed =>
                BadRequest(InvalidItineraryProblem(result.ValidationErrors!)),
            AdminCuratedItineraryResultStatus.NotFound => NotFound(NotFoundProblem()),
            _ => throw new InvalidOperationException("Unknown curated itinerary update result.")
        };
    }

    [HttpDelete("{id:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        var status = await service.DeleteAsync(id, cancellationToken);

        return status switch
        {
            DeleteAdminCuratedItineraryResultStatus.Success => NoContent(),
            DeleteAdminCuratedItineraryResultStatus.NotFound => NotFound(NotFoundProblem()),
            _ => throw new InvalidOperationException("Unknown curated itinerary delete result.")
        };
    }

    private ValidationProblemDetails InvalidQueryProblem(IDictionary<string, string[]> errors) =>
        ValidationProblem(errors, "Curated itinerary query is invalid.", "invalid_curated_itinerary_query");

    private ValidationProblemDetails InvalidItineraryProblem(IReadOnlyDictionary<string, string[]> errors) =>
        ValidationProblem(
            errors.ToDictionary(error => error.Key, error => error.Value),
            "Curated itinerary data is invalid.",
            "invalid_curated_itinerary");

    private ValidationProblemDetails ValidationProblem(IDictionary<string, string[]> errors, string title, string code)
    {
        var problem = new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = title,
            Type = "https://httpstatuses.com/400",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = code;
        return problem;
    }

    private ProblemDetails NotFoundProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status404NotFound,
            Title = "Curated itinerary was not found.",
            Type = "https://httpstatuses.com/404",
            Instance = HttpContext.Request.Path
        };

        problem.Extensions["code"] = "curated_itinerary_not_found";
        return problem;
    }
}
