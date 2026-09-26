using LocalMateAI.Application.Payments;

namespace LocalMateAI.Application.Interfaces.Payments;

public interface IPaymentGateway
{
    Task<PaymentLinkResult> CreatePaymentLinkAsync(
        PaymentLinkRequest request,
        CancellationToken cancellationToken = default);

    Task<PaymentGatewayOrderResult> GetPaymentAsync(
        long providerOrderCode,
        CancellationToken cancellationToken = default);

    Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(
        string rawPayload,
        CancellationToken cancellationToken = default);
}
