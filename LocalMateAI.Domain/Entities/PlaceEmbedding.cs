namespace LocalMateAI.Domain.Entities;

/// <summary>
/// Vector ngữ nghĩa của một địa điểm, do job nền tính từ tên, loại, tag và mô tả.
/// Không kế thừa BaseEntity vì khoá chính là PlaceId (mỗi địa điểm đúng một vector).
/// </summary>
public sealed class PlaceEmbedding
{
    public Guid PlaceId { get; set; }

    /// <summary>Tên model đã sinh vector. Khác model đang cấu hình thì vector này không dùng được.</summary>
    public required string Model { get; set; }

    /// <summary>SHA-256 (hex) của văn bản đã gửi đi, để biết nội dung địa điểm có đổi hay không.</summary>
    public required string ContentHash { get; set; }

    public required float[] Vector { get; set; }
    public DateTime UpdatedAt { get; set; }
}
