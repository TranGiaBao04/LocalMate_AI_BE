using LocalMateAI.Application.DTOs.Metro;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/metro")]
[AllowAnonymous]
public sealed class MetroController(IMetroTimetableService metroTimetableService) : ControllerBase
{
    // Không đặt ràng buộc {stationOrder:int}: sai kiểu sẽ trả 400 (model binding) thay vì 404 do không khớp route.
    [HttpGet("stations/{stationOrder}/departures")]
    [ProducesResponseType<MetroStationDeparturesResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MetroStationDeparturesResponse>> GetStationDeparturesAsync(
        int stationOrder,
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        var result = await metroTimetableService.GetStationDeparturesAsync(stationOrder, date, cancellationToken);
        return result.Status switch
        {
            MetroStationDeparturesResultStatus.Success => Ok(result.Response),
            MetroStationDeparturesResultStatus.ValidationFailed => CreateValidationProblem(result.ValidationErrors!),
            MetroStationDeparturesResultStatus.StationNotFound =>
                NotFound(CreateProblem(
                    StatusCodes.Status404NotFound,
                    "Metro station was not found.",
                    "metro_station_not_found")),
            _ => throw new InvalidOperationException("Unsupported metro station departures result status.")
        };
    }

    // BindRequired: thiếu from/to thì trả 400 thay vì âm thầm hiểu là 0.
    [HttpGet("journeys")]
    [ProducesResponseType<MetroJourneyResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<MetroJourneyResponse>> GetJourneyAsync(
        [FromQuery, BindRequired] int from,
        [FromQuery, BindRequired] int to,
        [FromQuery] DateOnly? date,
        CancellationToken cancellationToken)
    {
        var result = await metroTimetableService.GetJourneyAsync(from, to, date, cancellationToken);
        return result.Status switch
        {
            MetroJourneyResultStatus.Success => Ok(result.Response),
            MetroJourneyResultStatus.ValidationFailed => CreateValidationProblem(result.ValidationErrors!),
            MetroJourneyResultStatus.StationNotFound =>
                NotFound(CreateProblem(
                    StatusCodes.Status404NotFound,
                    "Metro station was not found.",
                    "metro_station_not_found")),
            _ => throw new InvalidOperationException("Unsupported metro journey result status.")
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
