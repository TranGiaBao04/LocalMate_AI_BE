using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces;
using LocalMateAI.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/places")]
public sealed class PlacesController(IPlaceQueryService placeQueryService) : ControllerBase
{
    [HttpGet("nearby")]
    [ProducesResponseType<NearbyPlacesResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<NearbyPlacesResponse>> GetNearby(
        [FromQuery] double latitude,
        [FromQuery] double longitude,
        [FromQuery] PlaceCategory? category,
        CancellationToken cancellationToken)
    {
        var result = await placeQueryService.GetPlacesNearUserAsync(latitude, longitude, category, cancellationToken);

        return Ok(result);
    }
}
