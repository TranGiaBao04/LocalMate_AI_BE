using LocalMateAI.Domain.Enums;

namespace LocalMateAI.Application.DTOs.Embeddings;

/// <summary>Nội dung của một địa điểm đang hiển thị, dùng để ghép văn bản gửi đi tính vector.</summary>
public sealed record PlaceEmbeddingSource(
    Guid PlaceId,
    string Name,
    PlaceCategory Category,
    string? Description,
    IReadOnlyList<string> TagNames);

/// <summary>Vector đang lưu của một địa điểm, không kèm mảng số (chỉ để so có cần tính lại không).</summary>
public sealed record PlaceEmbeddingState(Guid PlaceId, string Model, string ContentHash);

public sealed record PlaceEmbeddingEntry(Guid PlaceId, string Model, string ContentHash, float[] Vector);

public sealed record PlaceEmbeddingVector(Guid PlaceId, float[] Vector);

public enum PlaceEmbeddingSyncStatus
{
    Completed,
    NotConfigured,
    ProviderUnavailable
}

public sealed record PlaceEmbeddingSyncResult(PlaceEmbeddingSyncStatus Status, int Embedded, int Removed);
