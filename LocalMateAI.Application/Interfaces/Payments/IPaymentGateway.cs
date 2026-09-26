using LocalMateAI.Application.Payments;

namespace LocalMateAI.Application.Interfaces.Payments;

public interface IPaymentGateway
{
    Task<PaymentLinkResult> CreatePaymentLinkAsync(
        PaymentLinkRequest request,
        CancellationToken cancellationToken = default);
}
