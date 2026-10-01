using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Payments;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Domain.Entities;
using LocalMateAI.Domain.Enums;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Application.Services;

public sealed class PaymentWebhookService(
    IPaymentGateway paymentGateway,
    IPaymentSettlementService settlementService,
    IPaymentEvidenceRepository evidenceRepository,
    IOptions<PaymentEvidenceOptions> options,
    TimeProvider timeProvider,
    ILogger<PaymentWebhookService> logger) : IPaymentWebhookService
{
    public async Task<PaymentWebhookResult> ProcessAsync(
        string rawPayload,
        CancellationToken cancellationToken = default)
    {
        if (Encoding.UTF8.GetByteCount(rawPayload) > PaymentEvidenceOptions.MaximumRawPayloadBytes)
            return new(PaymentWebhookStatus.PayloadTooLarge);
        var verification = await paymentGateway.VerifyWebhookAsync(rawPayload, cancellationToken);
        if (!verification.IsValid || verification.Notification is null)
        {
            return new PaymentWebhookResult(PaymentWebhookStatus.InvalidSignature);
        }

        var notification = verification.Notification;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        // Preserve the exact UTF-8 body and its lowercase SHA-256 hex digest.
        var receipt = new PaymentWebhookReceipt
        {
            ProviderOrderCode = notification.ProviderOrderCode, Amount = notification.Amount,
            IsSuccessful = notification.IsSuccessful, ReceivedAt = now, RawPayload = rawPayload,
            RawPayloadSha256 = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(rawPayload))),
            RawPayloadRetainUntil = now.AddDays(options.Value.RawWebhookRetentionDays)
        };
        await evidenceRepository.SaveVerifiedReceiptAsync(receipt, cancellationToken);
        var settlement = await settlementService.ApplyVerifiedPaymentAsync(
            verification.Notification,
            new(PaymentStatusChangeSource.Webhook, WebhookReceiptId: receipt.Id),
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
