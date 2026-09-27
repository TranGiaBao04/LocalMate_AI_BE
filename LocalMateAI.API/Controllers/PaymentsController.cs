using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/payments")]
public sealed class PaymentsController(IPaymentWebhookService paymentWebhookService)
    : ControllerBase
{
    [HttpPost("payos/webhook")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PayOSWebhookAsync(CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(Request.Body);
        var rawPayload = await reader.ReadToEndAsync(cancellationToken);
        var result = await paymentWebhookService.ProcessAsync(rawPayload, cancellationToken);
        if (result.Status == PaymentWebhookStatus.InvalidSignature)
        {
            var problem = new ProblemDetails
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "The PayOS webhook signature is invalid.",
                Type = "https://httpstatuses.com/400",
                Instance = HttpContext.Request.Path
            };
            problem.Extensions["code"] = "invalid_webhook_signature";
            return BadRequest(problem);
        }

        return Ok(new { success = true });
    }
}
