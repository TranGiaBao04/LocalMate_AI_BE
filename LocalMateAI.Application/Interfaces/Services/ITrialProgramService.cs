using LocalMateAI.Application.DTOs.Subscription;

namespace LocalMateAI.Application.Interfaces.Services;

public interface ITrialProgramService
{
    Task<IReadOnlyList<TrialProgramResponse>> GetAllAsync(CancellationToken cancellationToken = default);

    Task<TrialProgramResult> CreateAsync(SaveTrialProgramRequest request, Guid actorUserId,
        CancellationToken cancellationToken = default);

    Task<TrialProgramResult> UpdateAsync(Guid id, SaveTrialProgramRequest request,
        CancellationToken cancellationToken = default);
}
