using LocalMateAI.Application.DTOs.MasterData;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/master-data")]
public sealed class MasterDataController(IMasterDataService masterDataService) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MasterDataResponse>(StatusCodes.Status200OK)]
    public async Task<ActionResult<MasterDataResponse>> Get(CancellationToken cancellationToken)
    {
        var result = await masterDataService.GetMasterDataAsync(cancellationToken);

        return Ok(result);
    }
}
