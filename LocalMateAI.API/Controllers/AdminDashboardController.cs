using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.Dashboard;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/admin/dashboard")]
[HasPermission(Permissions.ViewRevenue)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
public sealed class AdminDashboardController(IAdminDashboardService service) : ControllerBase
{
    [HttpGet("summary")]
    [ProducesResponseType<AdminDashboardSummaryResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetSummaryAsync([FromQuery] DashboardDateRangeQuery query,
        CancellationToken cancellationToken)
    {
        var result = await service.GetSummaryAsync(query, cancellationToken);
        return result.Status == AdminDashboardResultStatus.Success ? Ok(result.Response) : InvalidQuery(result);
    }

    [HttpGet("revenue-daily")]
    [ProducesResponseType<AdminDashboardRevenueDailyResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRevenueDailyAsync([FromQuery] DashboardDateRangeQuery query,
        CancellationToken cancellationToken)
    {
        var result = await service.GetRevenueDailyAsync(query, cancellationToken);
        return result.Status == AdminDashboardResultStatus.Success ? Ok(result.Response) : InvalidQuery(result);
    }

    [HttpGet("trips-finalized-daily")]
    [ProducesResponseType<AdminDashboardTripsFinalizedDailyResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTripsFinalizedDailyAsync([FromQuery] DashboardDateRangeQuery query,
        CancellationToken cancellationToken)
    {
        var result = await service.GetTripsFinalizedDailyAsync(query, cancellationToken);
        return result.Status == AdminDashboardResultStatus.Success ? Ok(result.Response) : InvalidQuery(result);
    }

    [HttpGet("top-stations")]
    [ProducesResponseType<AdminDashboardTopStationsResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetTopStationsAsync([FromQuery] DashboardTopStationsQuery query,
        CancellationToken cancellationToken)
    {
        var result = await service.GetTopStationsAsync(query, cancellationToken);
        return result.Status == AdminDashboardResultStatus.Success ? Ok(result.Response) : InvalidQuery(result);
    }

    [HttpGet("break-even")]
    [ProducesResponseType<AdminDashboardBreakEvenResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBreakEvenAsync([FromQuery] DashboardBreakEvenQuery query,
        CancellationToken cancellationToken)
    {
        var result = await service.GetBreakEvenAsync(query, cancellationToken);
        return result.Status == AdminDashboardResultStatus.Success ? Ok(result.Response) : InvalidQuery(result);
    }

    private BadRequestObjectResult InvalidQuery<T>(AdminDashboardResult<T> result)
    {
        var problem = new ValidationProblemDetails(result.ValidationErrors!.ToDictionary(e => e.Key, e => e.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Tham số dashboard không hợp lệ.",
            Instance = Request.Path
        };
        problem.Extensions["code"] = "invalid_dashboard_query";
        return BadRequest(problem);
    }
}
