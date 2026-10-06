using LocalMateAI.Application.DTOs.PublicStats;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/public/stats")]
public sealed class PublicStatsController(IPublicStatsService service) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PublicStatsResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<PublicStatsResponse>> GetAsync(CancellationToken cancellationToken) =>
        Ok(await service.GetAsync(cancellationToken));
}
