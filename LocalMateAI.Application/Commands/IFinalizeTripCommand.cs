using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Commands;

public interface IFinalizeTripCommand
{
    Task<FinalizeTripResult> ExecuteAsync(Guid userId, Guid tripId, FinalizeTripRequest request,
        CancellationToken ct = default) => request.FundingSource == "Normal"
            ? ExecuteAsync(userId, tripId, ct) : Task.FromResult(new FinalizeTripResult(FinalizeTripResultStatus.InvalidFunding));
    Task<FinalizeTripResult> ExecuteAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken = default);
}
