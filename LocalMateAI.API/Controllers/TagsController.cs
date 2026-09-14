using LocalMateAI.Application.DTOs.Tags;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/tags")]
[AllowAnonymous]
public sealed class TagsController(ITagService tagService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<TagResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<TagResponse>>> GetActiveTagsAsync(
        CancellationToken cancellationToken)
    {
        var tags = await tagService.GetActiveTagsAsync(cancellationToken);

        return Ok(tags);
    }
}
