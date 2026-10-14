using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Tests;

/// <summary>Service giả: ghi lại user được đề nghị cấp dùng thử; đặt Throw để giả lập DB lỗi.</summary>
internal sealed class RecordingSignupTrialService : ISignupTrialService
{
    public List<Guid> UserIds { get; } = [];
    public bool Throw { get; init; }

    public Task<SignupTrialOutcome> GrantAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        if (Throw)
        {
            throw new InvalidOperationException("Trial store is unavailable.");
        }

        UserIds.Add(userId);
        return Task.FromResult(SignupTrialOutcome.Granted);
    }
}
