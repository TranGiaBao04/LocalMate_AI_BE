namespace LocalMateAI.Application.Services;

/// <summary>
/// So khớp tên địa điểm để phát hiện nghi trùng: chuẩn hóa (trim + lowercase), rồi so khớp
/// chính xác hoặc một chuỗi chứa chuỗi kia. Yêu cầu độ dài tối thiểu để tránh khớp nhảm.
/// </summary>
public static class PlaceDuplicateMatcher
{
    public const int MinimumNormalizedLength = 3;

    public static bool IsSimilarName(string? candidateName, string inputName)
    {
        var candidate = Normalize(candidateName);
        var input = Normalize(inputName);

        if (candidate.Length < MinimumNormalizedLength || input.Length < MinimumNormalizedLength)
        {
            return false;
        }

        return string.Equals(candidate, input, StringComparison.Ordinal)
               || candidate.Contains(input, StringComparison.Ordinal)
               || input.Contains(candidate, StringComparison.Ordinal);
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty).Trim().ToLowerInvariant();
}
