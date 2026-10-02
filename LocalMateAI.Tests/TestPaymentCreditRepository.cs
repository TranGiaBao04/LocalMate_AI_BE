using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Payments;
using LocalMateAI.Domain.Entities;

namespace LocalMateAI.Tests;

internal sealed class TestPaymentCreditRepository(PaymentServiceTests.FakePaymentOrderRepository orders,
    TestSubscriptionRepository subscriptions) : IPaymentCreditRepository
{
    public List<PaymentOrderCredit> Rows { get; } = [];
    public Task AddClaimsAsync(IReadOnlyList<PaymentOrderCredit> claims, CancellationToken ct = default)
    {
        Rows.AddRange(claims);
        foreach (var claim in claims) orders.ReservedOrders.Add(claim.OrderId);
        return Task.CompletedTask;
    }
    public Task<IReadOnlyList<UpgradeSettlementSource>> LoadForSettlementAsync(Guid orderId, CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<UpgradeSettlementSource>>(Rows.Where(c => c.OrderId == orderId)
            .Select(c => new UpgradeSettlementSource(c, subscriptions.Periods.SingleOrDefault(p => p.Id == c.PeriodId))).ToArray());
    public Task<UpgradeReleaseCandidate?> GetReleaseCandidateAsync(Guid userId, Guid orderId, CancellationToken ct = default) =>
        Task.FromResult<UpgradeReleaseCandidate?>(null);
    public Task<UpgradeReleaseStatus> ReleaseAsync(UpgradeReleaseCandidate candidate, CreditReleaseEvidence evidence,
        CancellationToken ct = default) => throw new NotSupportedException();
}
