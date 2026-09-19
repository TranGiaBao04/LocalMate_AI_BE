using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Commands;

public interface IFinalizeTripCommand
{
    Task<FinalizeTripResult> ExecuteAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken = default);
}