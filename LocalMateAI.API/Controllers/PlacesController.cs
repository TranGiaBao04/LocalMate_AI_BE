using LocalMateAI.Application.DTOs.Places;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/places")]
public sealed class PlacesController(IPlaceQueryService placeQueryService) : ControllerBase
{
    [HttpGet("metro-clusters")]
    [AllowAnonymous]
    [ProducesResponseType<IReadOnlyList<MetroExperienceClusterResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<MetroExperienceClusterResponse>>> GetMetroClustersAsync(
        CancellationToken cancellationToken)
    {
        var result = await placeQueryService.GetMetroClustersAsync(cancellationToken);

        return Ok(result);
    }

    [HttpGet("nearby")]
    [AllowAnonymous]
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

    [HttpGet("{id:guid}")]
    [AllowAnonymous]
    [ProducesResponseType<PlaceDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PlaceDetailResponse>> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await placeQueryService.GetPlaceByIdAsync(id, cancellationToken);

        return result is null ? NotFound() : Ok(result);
    }
}
