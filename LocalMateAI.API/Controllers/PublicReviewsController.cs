using LocalMateAI.Application.DTOs.PublicReviews;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/public/reviews")]
public sealed class PublicReviewsController(IPublicReviewService service) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<PublicReviewsResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetAsync([FromQuery] PublicReviewQuery query, CancellationToken cancellationToken)
    {
        var result = await service.GetAsync(query, cancellationToken);
        if (result.Status == PublicReviewsResultStatus.Success)
        {
            return Ok(result.Response);
        }

        var problem = new ValidationProblemDetails(result.ValidationErrors!.ToDictionary(e => e.Key, e => e.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Tham số danh sách đánh giá không hợp lệ.",
            Instance = Request.Path
        };
        problem.Extensions["code"] = "invalid_public_review_query";
        return BadRequest(problem);
    }
}
