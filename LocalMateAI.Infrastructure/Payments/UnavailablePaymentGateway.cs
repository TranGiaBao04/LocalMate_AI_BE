using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Payments;

namespace LocalMateAI.Infrastructure.Payments;

public sealed class UnavailablePaymentGateway : IPaymentGateway
{
    public Task<PaymentLinkResult> CreatePaymentLinkAsync(
        PaymentLinkRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PaymentLinkResult.Unavailable());

    public Task<PaymentGatewayOrderResult> GetPaymentAsync(
        long providerOrderCode,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PaymentGatewayOrderResult.Unavailable(providerOrderCode));

    public Task<PaymentWebhookVerificationResult> VerifyWebhookAsync(
        string rawPayload,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PaymentWebhookVerificationResult.Invalid());
}
