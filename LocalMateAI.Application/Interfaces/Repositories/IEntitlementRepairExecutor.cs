using LocalMateAI.Application.Payments;
namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IEntitlementRepairExecutor
{
    Task<EntitlementRepairResponse?> ExecuteAsync(Guid orderId, Guid actorId, string reason, CancellationToken cancellationToken = default);
}
