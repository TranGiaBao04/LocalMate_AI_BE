using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IPaymentCreditRepository
{
    Task<IReadOnlyList<UpgradeSettlementSource>> LoadForSettlementAsync(Guid orderId,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();
    Task AddClaimsAsync(IReadOnlyList<PaymentOrderCredit> claims, CancellationToken cancellationToken = default);
    Task<UpgradeReleaseCandidate?> GetReleaseCandidateAsync(Guid userId, Guid orderId, CancellationToken cancellationToken = default);
    Task<UpgradeReleaseStatus> ReleaseAsync(UpgradeReleaseCandidate expected, CreditReleaseEvidence evidence,
        CancellationToken cancellationToken = default);
}
