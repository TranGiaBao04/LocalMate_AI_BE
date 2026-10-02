using LocalMateAI.API.Authorization;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Entities;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[Route("api/admin/transactions")]
[HasPermission(Permissions.ManagePlans)]
public sealed class AdminEntitlementRepairController(IEntitlementRepairService repair) : ControllerBase
{
    [HttpPost("{id:guid}/repair-entitlement")]
    [ProducesResponseType<EntitlementRepairResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RepairAsync(Guid id, [FromBody] EntitlementRepairRequest? request, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var actorId) || actorId == Guid.Empty) return Unauthorized();
        // Keep malformed JSON and semantic validation on the same stable error contract.
        if (!ModelState.IsValid || request is null) return Failure(400, "invalid_entitlement_repair_request");
        var result = await repair.RepairAsync(id, actorId, request.Reason, cancellationToken);
        if (result.InvalidRequest) return Failure(400, "invalid_entitlement_repair_request");
        if (result.Response is not { } response) return Failure(404, "transaction_not_found");
        if (response.Result is EntitlementRepairOutcome.Repaired or EntitlementRepairOutcome.AlreadyGranted) return Ok(response);
        return Failure(409, response.Result == EntitlementRepairOutcome.Conflict
            ? "entitlement_repair_conflict" : "entitlement_repair_not_eligible", response);
    }

    private IActionResult Failure(int status, string code, EntitlementRepairResponse? response = null)
    {
        var problem = new ProblemDetails
        {
            Status = status, Title = "Entitlement repair could not be completed.", Instance = Request.Path,
            Extensions = { ["code"] = code }
        };
        if (response is not null)
        {
            problem.Extensions["result"] = response.Result.ToString();
            problem.Extensions["orderId"] = response.OrderId;
            problem.Extensions["subscriptionPeriodId"] = response.SubscriptionPeriodId;
            problem.Extensions["startsAt"] = response.StartsAt;
            problem.Extensions["endsAt"] = response.EndsAt;
            problem.Extensions["auditId"] = response.AuditId;
            problem.Extensions["decisionCode"] = response.DecisionCode;
            problem.Extensions["reconstructionMode"] = response.ReconstructionMode;
        }
        return StatusCode(status, problem);
    }
}
