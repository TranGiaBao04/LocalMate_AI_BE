using System.Security.Cryptography;
using System.Text;
using LocalMateAI.Application.DTOs.Embeddings;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.Services;

// Nơi DUY NHẤT ghép văn bản của địa điểm để tính vector. Đổi định dạng ở đây làm mã băm của mọi địa điểm
// thay đổi (job tính lại toàn bộ) và phải đo lại ngưỡng Semantic.MinSimilarityPercent.
public static class PlaceEmbeddingTextBuilder
{
    public static EmbeddingDocument Build(PlaceEmbeddingSource source)
    {
        var parts = new List<string> { $"Loại: {CategoryLabel(source.Category)}." };

        // Sắp theo mã ký tự để thứ tự tag không phụ thuộc collation của DB: cùng nội dung thì cùng mã băm.
        var tags = source.TagNames.Order(StringComparer.Ordinal).ToArray();
        if (tags.Length > 0)
        {
            parts.Add($"Tag: {string.Join(", ", tags)}.");
        }

        var description = source.Description?.Trim();
        if (!string.IsNullOrEmpty(description))
        {
            parts.Add(description);
        }

        return new EmbeddingDocument(source.Name.Trim(), string.Join(' ', parts));
    }

    public static string Hash(EmbeddingDocument document) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{document.Title}\n{document.Text}")));

    // Nhãn đã dùng khi đo ngưỡng ở GĐ 0 (khác nhãn trong mail lịch trình), giữ nguyên để ngưỡng còn đúng.
    private static string CategoryLabel(PlaceCategory category) => category switch
    {
        PlaceCategory.Cafe => "Quán cà phê",
        PlaceCategory.Food => "Ăn uống",
        PlaceCategory.Culture => "Văn hoá",
        PlaceCategory.CheckIn => "Check-in",
        _ => category.ToString()
    };
}
