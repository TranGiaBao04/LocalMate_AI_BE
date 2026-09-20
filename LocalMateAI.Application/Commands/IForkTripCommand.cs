using LocalMateAI.Application.DTOs.Trips;

namespace LocalMateAI.Application.Commands;

public interface IForkTripCommand
{
    Task<ForkTripResult> ExecuteAsync(
        Guid userId,
        Guid tripId,
        CancellationToken cancellationToken = default);
}