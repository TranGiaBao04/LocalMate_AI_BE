using LocalMateAI.Application.Payments;
namespace LocalMateAI.Application.Interfaces.Services;

public interface IEntitlementRepairService
{
    Task<EntitlementRepairServiceResult> RepairAsync(Guid orderId, Guid actorId, string? reason, CancellationToken cancellationToken = default);
}
