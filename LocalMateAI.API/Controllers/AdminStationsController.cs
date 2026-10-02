using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.Stations;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/admin/stations")]
[HasPermission(Permissions.ManagePlaces)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
public sealed class AdminStationsController(IAdminStationService service) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<AdminStationsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<AdminStationsResponse>> GetAsync(CancellationToken cancellationToken)
    {
        return Ok(await service.GetStationsAsync(cancellationToken));
    }
}
