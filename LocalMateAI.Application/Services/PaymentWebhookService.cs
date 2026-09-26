using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Payments;
using Microsoft.Extensions.Logging;

namespace LocalMateAI.Application.Services;

public sealed class PaymentWebhookService(
    IPaymentGateway paymentGateway,
    IPaymentSettlementService settlementService,
    ILogger<PaymentWebhookService> logger) : IPaymentWebhookService
{
    public async Task<PaymentWebhookResult> ProcessAsync(
        string rawPayload,
        CancellationToken cancellationToken = default)
    {
        var verification = await paymentGateway.VerifyWebhookAsync(rawPayload, cancellationToken);
        if (!verification.IsValid || verification.Notification is null)
        {
            return new PaymentWebhookResult(PaymentWebhookStatus.InvalidSignature);
        }

        var settlement = await settlementService.ApplyVerifiedPaymentAsync(
            verification.Notification,
            cancellationToken);
        if (settlement.Status == PaymentSettlementStatus.UnknownOrder)
        {
            logger.LogWarning(
                "Acknowledged verified PayOS webhook for unknown provider order {ProviderOrderCode}",
                verification.Notification.ProviderOrderCode);
        }

        return new PaymentWebhookResult(PaymentWebhookStatus.Acknowledged);
    }
}
