using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Tests;

// Bộ chấm điểm theo nghĩa giả, dùng chung cho các test cần ISemanticPlaceScorer.
// Scores = null (mặc định) nghĩa là tính năng không dùng được, như khi thiếu khoá hoặc nhà cung cấp lỗi.
internal sealed class FakeSemanticPlaceScorer : ISemanticPlaceScorer
{
    public IReadOnlyDictionary<Guid, double>? Scores { get; set; }
    public List<(string Text, TimeSpan Timeout)> Calls { get; } = [];

    public Task<IReadOnlyDictionary<Guid, double>?> ScoreAsync(
        string text,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        Calls.Add((text, timeout));
        return Task.FromResult(Scores);
    }
}
