using LocalMateAI.Application.Interfaces.Payments;
using LocalMateAI.Application.Payments;

namespace LocalMateAI.Infrastructure.Payments;

public sealed class UnavailablePaymentGateway : IPaymentGateway
{
    public Task<PaymentLinkResult> CreatePaymentLinkAsync(
        PaymentLinkRequest request,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(PaymentLinkResult.Unavailable());
}
