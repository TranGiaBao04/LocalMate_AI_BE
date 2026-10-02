using LocalMateAI.API.Authorization;
using LocalMateAI.Application.DTOs.Common;
using LocalMateAI.Application.DTOs.Payments;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Security;
using LocalMateAI.Application.Services;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/admin/transactions")]
[HasPermission(Permissions.ViewRevenue)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
public sealed class AdminTransactionsController(IAdminTransactionService service) : ControllerBase
{
    [HttpGet("{id:guid}")]
    [ProducesResponseType<AdminTransactionDetailResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DetailAsync(Guid id, CancellationToken cancellationToken)
    {
        var detail = await service.GetDetailAsync(id, cancellationToken);
        if (detail is not null) return Ok(detail);
        var problem = new ProblemDetails
        {
            Status = 404, Title = "Transaction not found.", Instance = Request.Path
        };
        problem.Extensions["code"] = "transaction_not_found";
        return NotFound(problem);
    }

    [HttpGet]
    [ProducesResponseType<PagedResult<AdminTransactionResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ListAsync([FromQuery] AdminTransactionQuery query, CancellationToken cancellationToken)
    {
        var result = await service.GetTransactionsAsync(query, cancellationToken);
        return result.Status == AdminTransactionResultStatus.Success ? Ok(result.Response) : Failure(result);
    }

    [HttpGet("summary")]
    [ProducesResponseType<AdminTransactionSummary>(StatusCodes.Status200OK)]
    public async Task<IActionResult> SummaryAsync([FromQuery] AdminTransactionFilterQuery query, CancellationToken cancellationToken)
    {
        var result = await service.GetSummaryAsync(query, cancellationToken);
        return result.Status == AdminTransactionResultStatus.Success ? Ok(result.Response) : Failure(result);
    }

    [HttpGet("export.csv")]
    [ProducesResponseType<FileContentResult>(StatusCodes.Status200OK)]
    public async Task<IActionResult> ExportAsync([FromQuery] AdminTransactionFilterQuery query, CancellationToken cancellationToken)
    {
        var result = await service.ExportAsync(query, cancellationToken);
        return result.Status == AdminTransactionResultStatus.Success
            ? File(result.Response!.Content, "text/csv; charset=utf-8", result.Response.FileName) : Failure(result);
    }

    private IActionResult Failure<T>(AdminTransactionResult<T> result)
    {
        if (result.Status == AdminTransactionResultStatus.InvalidQuery)
        {
            var problem = new ValidationProblemDetails(result.ValidationErrors!.ToDictionary(e => e.Key, e => e.Value))
            { Status = 400, Title = "Transaction query is invalid.", Instance = Request.Path };
            problem.Extensions["code"] = "invalid_transaction_query";
            return BadRequest(problem);
        }
        var limit = new ProblemDetails { Status = 400, Title = "Too many transactions to export. Narrow the filters.", Instance = Request.Path };
        limit.Extensions["code"] = "transaction_export_limit_exceeded";
        limit.Extensions["maxRows"] = AdminTransactionService.MaxExportRows;
        limit.Extensions["matchingRows"] = result.MatchingRows;
        return BadRequest(limit);
    }
}
