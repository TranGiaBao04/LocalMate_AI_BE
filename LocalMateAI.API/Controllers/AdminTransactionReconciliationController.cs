using LocalMateAI.API.Authorization;
using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Security;
using LocalMateAI.Domain.Enums;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/admin/transactions")]
[HasPermission(Permissions.ManagePlans)]
public sealed class AdminTransactionReconciliationController(IPaymentReconciliationService reconciliation) : ControllerBase
{
    [HttpPost("{id:guid}/reconcile")]
    [ProducesResponseType<PaymentReconciliationResult>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status502BadGateway)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> ReconcileAsync(Guid id, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(User.FindFirst("sub")?.Value, out var actorId) || actorId == Guid.Empty) return Unauthorized();
        var result = await reconciliation.ReconcileAsync(id, new(PaymentStatusChangeSource.AdminReconcile, actorId), cancellationToken);
        var failure = result.Status switch
        {
            PaymentReconciliationStatus.NotFound => (404, "transaction_not_found", "Transaction not found."),
            PaymentReconciliationStatus.ProviderUnavailable => (503, "payment_provider_unavailable", "Payment provider is currently unavailable."),
            PaymentReconciliationStatus.ProviderMismatch => (502, "payment_provider_mismatch", "Payment provider returned a mismatched order."),
            _ => (0, "", "")
        };
        if (failure.Item1 == 0) return Ok(result);
        return StatusCode(failure.Item1, new ProblemDetails
        {
            Status = failure.Item1, Title = failure.Item3, Instance = Request.Path,
            Extensions = { ["code"] = failure.Item2 }
        });
    }
}
