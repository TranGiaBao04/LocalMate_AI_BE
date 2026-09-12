using LocalMateAI.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/trips/routes")]
public class RouteEstimateController(
    IRouteEstimateService routeEstimateService,
    IMetroWalkingRouter metroWalkingRouter,
    ICoordinatesValidationService validationService) : ControllerBase
{
    /// <summary>
    /// Tính toán khoảng cách và thời gian di chuyển ước tính giữa 2 địa điểm (BE-62)
    /// </summary>
    [HttpGet("estimate")]
    public IActionResult EstimateRoute(
        [FromQuery] double originLat,
        [FromQuery] double originLng,
        [FromQuery] double destLat,
        [FromQuery] double destLng,
        [FromQuery] string? originName = null,
        [FromQuery] string? destName = null)
    {
        var originValidation = validationService.ValidateCoordinate(originLat, originLng);
        if (!originValidation.IsValid)
        {
            return BadRequest(new { error = $"Origin invalid: {originValidation.Reason}" });
        }

        var destValidation = validationService.ValidateCoordinate(destLat, destLng);
        if (!destValidation.IsValid)
        {
            return BadRequest(new { error = $"Destination invalid: {destValidation.Reason}" });
        }

        var result = routeEstimateService.EstimateRoute(
            originLat, originLng, destLat, destLng, originName, destName);

        return Ok(result);
    }

    /// <summary>
    /// Tìm ga Metro số 1 gần nhất và tính thời gian đi bộ kết nối (BE-63)
    /// </summary>
    [HttpGet("metro-walk")]
    public IActionResult GetNearestMetroWalkRoute(
        [FromQuery] double lat,
        [FromQuery] double lng,
        [FromQuery] string? placeName = null)
    {
        var validation = validationService.ValidateCoordinate(lat, lng);
        if (!validation.IsValid)
        {
            return BadRequest(new { error = validation.Reason });
        }

        var result = metroWalkingRouter.FindNearestMetroStationRoute(lat, lng, placeName);
        return Ok(result);
    }

    /// <summary>
    /// Lấy danh sách 14 ga Metro Số 1 (Bến Thành - Suối Tiên)
    /// </summary>
    [HttpGet("metro-stations")]
    public IActionResult GetMetroStations()
    {
        var stations = metroWalkingRouter.GetMetroLine1Stations();
        return Ok(stations);
    }
}
