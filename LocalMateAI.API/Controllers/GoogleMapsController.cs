using LocalMateAI.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/maps")]
public class GoogleMapsController(
    IGoogleMapsUrlBuilderService mapsUrlBuilder,
    ICoordinatesValidationService validationService) : ControllerBase
{
    /// <summary>
    /// Sinh URL Google Maps Search/Directions chính thức (BE-60)
    /// </summary>
    [HttpGet("build-url")]
    public IActionResult BuildUrl(
        [FromQuery] double lat,
        [FromQuery] double lng,
        [FromQuery] string? placeName = null,
        [FromQuery] double? destLat = null,
        [FromQuery] double? destLng = null,
        [FromQuery] string? destName = null,
        [FromQuery] string travelMode = "walking")
    {
        var validation = validationService.ValidateCoordinate(lat, lng);
        if (!validation.IsValid)
        {
            return BadRequest(new { error = validation.Reason });
        }

        if (destLat.HasValue && destLng.HasValue)
        {
            var destValidation = validationService.ValidateCoordinate(destLat.Value, destLng.Value);
            if (!destValidation.IsValid)
            {
                return BadRequest(new { error = destValidation.Reason });
            }

            var directionsUrl = mapsUrlBuilder.BuildDirectionsUrl(
                lat, lng, destLat.Value, destLng.Value, placeName, destName, travelMode);

            return Ok(new { type = "directions", url = directionsUrl });
        }

        var searchUrl = mapsUrlBuilder.BuildSearchUrl(lat, lng, placeName);
        return Ok(new { type = "search", url = searchUrl });
    }

    /// <summary>
    /// Kiểm tra tính hợp lệ của tọa độ vĩ độ/kinh độ tại TP.HCM (BE-61)
    /// </summary>
    [HttpGet("validate-coordinate")]
    public IActionResult ValidateCoordinate([FromQuery] double lat, [FromQuery] double lng)
    {
        var (isValid, reason) = validationService.ValidateCoordinate(lat, lng);
        return Ok(new { lat, lng, isValid, message = reason });
    }
}
