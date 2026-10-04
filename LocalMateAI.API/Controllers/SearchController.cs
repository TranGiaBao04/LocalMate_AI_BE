using LocalMateAI.Application.DTOs.Search;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/search")]
public sealed class SearchController(ISearchService service) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    [ProducesResponseType<SearchResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> SearchAsync([FromQuery] SearchQuery query, CancellationToken cancellationToken)
    {
        var result = await service.SearchAsync(query, cancellationToken);
        if (result.Status == SearchResultStatus.Success)
        {
            return Ok(result.Response);
        }

        var problem = new ValidationProblemDetails(result.ValidationErrors!.ToDictionary(e => e.Key, e => e.Value))
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Tham số tìm kiếm không hợp lệ.",
            Instance = Request.Path
        };
        problem.Extensions["code"] = "invalid_search_query";
        return BadRequest(problem);
    }
}
