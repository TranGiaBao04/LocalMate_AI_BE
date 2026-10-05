using LocalMateAI.Application.DTOs.Embeddings;

namespace LocalMateAI.Application.Interfaces.Repositories;

public interface IPlaceEmbeddingRepository
{
    /// <summary>Địa điểm Active chưa xoá, kèm tên các tag đang bật.</summary>
    Task<IReadOnlyList<PlaceEmbeddingSource>> GetSourcesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<PlaceEmbeddingState>> GetStatesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Vector do đúng model này sinh. Không lọc trạng thái địa điểm:
    /// nơi dùng phải tự lọc địa điểm đang hiển thị.
    /// </summary>
    Task<IReadOnlyList<PlaceEmbeddingVector>> GetVectorsAsync(
        string model,
        CancellationToken cancellationToken = default);

    /// <summary>Ghi đè vector theo PlaceId. Bỏ qua địa điểm không còn Active. Trả số dòng đã ghi.</summary>
    Task<int> UpsertAsync(
        IReadOnlyList<PlaceEmbeddingEntry> entries,
        DateTime updatedAt,
        CancellationToken cancellationToken = default);

    /// <summary>Xoá vector của địa điểm không còn Active hoặc đã xoá mềm. Trả số dòng đã xoá.</summary>
    Task<int> DeleteForInactivePlacesAsync(CancellationToken cancellationToken = default);
}
