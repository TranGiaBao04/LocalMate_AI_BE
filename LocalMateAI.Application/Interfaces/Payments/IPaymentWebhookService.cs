using LocalMateAI.Application.Payments;

namespace LocalMateAI.Application.Interfaces.Payments;

public interface IPaymentWebhookService
{
    Task<PaymentWebhookResult> ProcessAsync(
        string rawPayload,
        CancellationToken cancellationToken = default);
}
