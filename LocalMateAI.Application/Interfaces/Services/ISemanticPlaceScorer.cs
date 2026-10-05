namespace LocalMateAI.Application.Interfaces.Services;

public interface ISemanticPlaceScorer
{
    /// <summary>
    /// Điểm tương đồng (0–1) giữa câu của user và từng địa điểm đã có vector.
    /// Trả null khi không dùng được (thiếu khoá, chưa có vector, nhà cung cấp lỗi hoặc quá giờ):
    /// nơi gọi phải chạy tiếp như khi chưa có tính năng này.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, double>?> ScoreAsync(
        string text,
        TimeSpan timeout,
        CancellationToken cancellationToken = default);
}
