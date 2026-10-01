namespace LocalMateAI.Application.Interfaces.Payments;

public interface IPaymentReconciliationLeaseProvider
{
    Task<IAsyncDisposable?> TryAcquireAsync(CancellationToken cancellationToken = default);
}
