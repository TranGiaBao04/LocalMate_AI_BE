using LocalMateAI.Application.DTOs.Subscription;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IPaymentService
{
    Task<PaymentIntentResult> CheckoutAsync(
        Guid userId,
        string? planCode,
        CancellationToken cancellationToken = default);

    Task<PaymentIntentResult> RenewAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<PaymentOrderLookupResult> GetOrderAsync(
        Guid userId,
        Guid orderId,
        CancellationToken cancellationToken = default);
}
