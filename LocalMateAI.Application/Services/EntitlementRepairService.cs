using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Application.Payments;
namespace LocalMateAI.Application.Services;

public sealed class EntitlementRepairService(IEntitlementRepairExecutor executor) : IEntitlementRepairService
{
    public async Task<EntitlementRepairServiceResult> RepairAsync(Guid orderId, Guid actorId, string? reason,
        CancellationToken cancellationToken = default)
    {
        if (actorId == Guid.Empty || !EntitlementRepairReason.TryNormalize(reason, out var normalized)) return new(true);
        return new(false, await executor.ExecuteAsync(orderId, actorId, normalized, cancellationToken));
    }
}
