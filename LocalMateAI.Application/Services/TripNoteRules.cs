namespace LocalMateAI.Application.Services;

// Nơi DUY NHẤT chuẩn hoá ghi chú của chuyến đi và tính điểm ghi chú của từng ứng viên.
public static class TripNoteRules
{
    public const int MaxLength = 300;

    // Điểm tương đồng vượt sàn từng này điểm phần trăm thì coi như khớp ghi chú tối đa.
    public const int FullScoreMarginPercent = 10;

    /// <summary>Cắt khoảng trắng hai đầu, gộp khoảng trắng liền nhau; không có chữ nào thì trả null.</summary>
    public static string? Normalize(string? note) =>
        string.IsNullOrWhiteSpace(note)
            ? null
            : string.Join(' ', note.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Điểm ghi chú (0–1) của những ứng viên đạt sàn: 0 ở đúng sàn, tăng đều, tối đa 1 khi vượt sàn
    /// FullScoreMarginPercent. Ứng viên dưới sàn hoặc chưa có vector không có trong kết quả.
    /// Kết quả rỗng nghĩa là ghi chú không áp dụng được.
    /// </summary>
    public static IReadOnlyDictionary<Guid, double> Score(
        IEnumerable<Guid> candidatePlaceIds,
        IReadOnlyDictionary<Guid, double> similarities,
        int minSimilarityPercent)
    {
        var floor = minSimilarityPercent / 100d;
        var margin = FullScoreMarginPercent / 100d;
        var scores = new Dictionary<Guid, double>();

        foreach (var placeId in candidatePlaceIds)
        {
            if (similarities.TryGetValue(placeId, out var similarity) && similarity >= floor)
            {
                scores[placeId] = Math.Min(1d, (similarity - floor) / margin);
            }
        }

        return scores;
    }
}
