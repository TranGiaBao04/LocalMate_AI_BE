using LocalMateAI.Application.DTOs.Embeddings;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;

namespace LocalMateAI.Application.Services;

public sealed class PlaceEmbeddingSyncService(
    IPlaceEmbeddingRepository repository,
    IEmbeddingClient embeddingClient,
    TimeProvider timeProvider) : IPlaceEmbeddingSyncService
{
    public const int BatchSize = 50;

    public async Task<PlaceEmbeddingSyncResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        if (!embeddingClient.IsConfigured)
        {
            return new PlaceEmbeddingSyncResult(PlaceEmbeddingSyncStatus.NotConfigured, 0, 0);
        }

        // Dọn trước: không phụ thuộc nhà cung cấp, nên vẫn chạy khi lần gọi bên dưới lỗi.
        var removed = await repository.DeleteForInactivePlacesAsync(cancellationToken);

        var model = embeddingClient.Model;
        var states = (await repository.GetStatesAsync(cancellationToken)).ToDictionary(state => state.PlaceId);

        var stale = (await repository.GetSourcesAsync(cancellationToken))
            .Select(source =>
            {
                var document = PlaceEmbeddingTextBuilder.Build(source);
                return (source.PlaceId, Document: document, Hash: PlaceEmbeddingTextBuilder.Hash(document));
            })
            .Where(item => !states.TryGetValue(item.PlaceId, out var state)
                || state.Model != model
                || state.ContentHash != item.Hash)
            .ToArray();

        var embedded = 0;

        try
        {
            // Ghi theo từng lô để lô sau lỗi thì các lô trước vẫn được giữ.
            foreach (var batch in stale.Chunk(BatchSize))
            {
                var vectors = await embeddingClient.EmbedDocumentsAsync(
                    batch.Select(item => item.Document).ToArray(),
                    cancellationToken);

                var entries = batch
                    .Select((item, index) => new PlaceEmbeddingEntry(item.PlaceId, model, item.Hash, vectors[index]))
                    .ToArray();

                embedded += await repository.UpsertAsync(
                    entries,
                    timeProvider.GetUtcNow().UtcDateTime,
                    cancellationToken);
            }
        }
        catch (EmbeddingUnavailableException)
        {
            return new PlaceEmbeddingSyncResult(PlaceEmbeddingSyncStatus.ProviderUnavailable, embedded, removed);
        }

        return new PlaceEmbeddingSyncResult(PlaceEmbeddingSyncStatus.Completed, embedded, removed);
    }
}
