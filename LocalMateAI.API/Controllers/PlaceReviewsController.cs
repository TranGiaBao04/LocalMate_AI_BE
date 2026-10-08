using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.PlaceReviews;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/places/{id:guid}/reviews")]
public sealed class PlaceReviewsController(IPlaceReviewQueryService service) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PagedResult<PublicPlaceReviewResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetAsync(
        Guid id,
        [FromQuery] PlaceReviewQuery query,
        CancellationToken cancellationToken)
    {
        var result = await service.GetPlaceReviewsAsync(id, query, cancellationToken);

        switch (result.Status)
        {
            case PlaceReviewListResultStatus.Success:
                return Ok(result.Response);

            case PlaceReviewListResultStatus.PlaceNotFound:
                var notFound = new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "Không tìm thấy địa điểm.",
                    Type = "https://httpstatuses.com/404",
                    Instance = Request.Path
                };
                notFound.Extensions["code"] = "place_not_found";
                return NotFound(notFound);

            case PlaceReviewListResultStatus.InvalidQuery:
                var invalid = new ValidationProblemDetails(
                    result.ValidationErrors!.ToDictionary(error => error.Key, error => error.Value))
                {
                    Status = StatusCodes.Status400BadRequest,
                    Title = "Tham số danh sách đánh giá không hợp lệ.",
                    Instance = Request.Path
                };
                invalid.Extensions["code"] = "invalid_place_review_query";
                return BadRequest(invalid);

            default:
                throw new InvalidOperationException("Unsupported place review list result status.");
        }
    }
}
