using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text;

namespace LocalMateAI.API.Controllers;

[ApiController]
[Route("api/payments")]
public sealed class PaymentsController(IPaymentWebhookService paymentWebhookService)
    : ControllerBase
{
    [HttpPost("payos/webhook")]
    [AllowAnonymous]
    [Consumes("application/json")]
    [RequestSizeLimit(PaymentEvidenceOptions.MaximumRawPayloadBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status413PayloadTooLarge)]
    public async Task<IActionResult> PayOSWebhookAsync(CancellationToken cancellationToken)
    {
        const int limit = PaymentEvidenceOptions.MaximumRawPayloadBytes;
        if (Request.ContentLength > limit) return TooLarge();
        using var body = new MemoryStream();
        var buffer = new byte[8192];
        int read;
        try
        {
            while ((read = await Request.Body.ReadAsync(buffer.AsMemory(0,
                       (int)Math.Min(buffer.Length, limit + 1 - body.Length)), cancellationToken)) > 0)
            {
                body.Write(buffer, 0, read);
                if (body.Length > limit) return TooLarge();
            }
        }
        // Kestrel may reject chunked bodies before the bounded reader reaches limit + 1.
        catch (BadHttpRequestException e) when (e.StatusCode == StatusCodes.Status413PayloadTooLarge)
        { return TooLarge(); }
        string rawPayload;
        try { rawPayload = new UTF8Encoding(false, true).GetString(body.ToArray()); }
        catch (DecoderFallbackException) { return BadRequest(new { code = "invalid_webhook_encoding" }); }
        var result = await paymentWebhookService.ProcessAsync(rawPayload, cancellationToken);
        if (result.Status == PaymentWebhookStatus.PayloadTooLarge) return TooLarge();
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

    private IActionResult TooLarge() => StatusCode(StatusCodes.Status413PayloadTooLarge,
        new ProblemDetails { Status = 413, Title = "Webhook payload exceeds 64 KiB.",
            Extensions = { ["code"] = "webhook_payload_too_large" } });
}
