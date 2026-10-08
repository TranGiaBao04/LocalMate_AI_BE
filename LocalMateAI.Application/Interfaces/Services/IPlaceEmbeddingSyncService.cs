using LocalMateAI.Application.DTOs.Embeddings;

namespace LocalMateAI.Application.Interfaces.Services;

public interface IPlaceEmbeddingSyncService
{
    Task<PlaceEmbeddingSyncResult> SyncAsync(CancellationToken cancellationToken = default);
}
