using LocalMateAI.Application.DTOs.Itineraries;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/itineraries")]
public sealed class ItinerariesController(ICuratedItineraryService curatedItineraryService) : ControllerBase
{
    [HttpGet("curated")]
    [ProducesResponseType<IReadOnlyList<CuratedItineraryResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<CuratedItineraryResponse>>> GetCurated(CancellationToken cancellationToken)
    {
        var result = await curatedItineraryService.GetCuratedItinerariesAsync(cancellationToken);

        return Ok(result);
    }
}
