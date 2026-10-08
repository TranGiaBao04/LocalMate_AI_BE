using LocalMateAI.Application.DTOs.Embeddings;
using LocalMateAI.Application.Interfaces.Repositories;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace LocalMateAI.Application.Services;

public sealed class SemanticPlaceScorer(
    IEmbeddingClient embeddingClient,
    IPlaceEmbeddingRepository repository,
    IMemoryCache cache,
    TimeProvider timeProvider,
    ILogger<SemanticPlaceScorer> logger) : ISemanticPlaceScorer
{
    public static readonly TimeSpan SearchTimeout = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan PlanningTimeout = TimeSpan.FromMilliseconds(2500);
    public static readonly TimeSpan QueryCacheDuration = TimeSpan.FromHours(1);
    public static readonly TimeSpan PlaceVectorCacheDuration = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan FailureCooldown = TimeSpan.FromSeconds(60);

    private const string CooldownCacheKey = "semantic:cooldown-until";

    public async Task<IReadOnlyDictionary<Guid, double>?> ScoreAsync(
        string text,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        var query = Normalize(text);

        if (query.Length == 0 || !embeddingClient.IsConfigured || IsCoolingDown())
        {
            return null;
        }

        // Đọc vector địa điểm trước: chưa có vector nào thì khỏi tốn một lần gọi nhà cung cấp.
        var places = await GetPlaceVectorsAsync(cancellationToken);
        if (places.Count == 0)
        {
            return null;
        }

        var queryVector = await GetQueryVectorAsync(query, timeout, cancellationToken);
        if (queryVector is null)
        {
            return null;
        }

        // Hai phía đều đã chuẩn hoá độ dài 1 nên tích vô hướng chính là cosine.
        return places.ToDictionary(place => place.PlaceId, place => Dot(queryVector, place.Vector));
    }

    private async Task<IReadOnlyList<PlaceEmbeddingVector>> GetPlaceVectorsAsync(CancellationToken cancellationToken)
    {
        var cacheKey = $"semantic:place-vectors:{embeddingClient.Model}";

        if (cache.TryGetValue(cacheKey, out IReadOnlyList<PlaceEmbeddingVector>? cached) && cached is not null)
        {
            return cached;
        }

        var vectors = await repository.GetVectorsAsync(embeddingClient.Model, cancellationToken);

        // Không cache danh sách rỗng: job đồng bộ có thể vừa chạy xong ngay sau đó.
        if (vectors.Count > 0)
        {
            cache.Set(cacheKey, vectors, PlaceVectorCacheDuration);
        }

        return vectors;
    }

    private async Task<float[]?> GetQueryVectorAsync(string query, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var cacheKey = $"semantic:query:{embeddingClient.Model}:{query}";

        if (cache.TryGetValue(cacheKey, out float[]? cached) && cached is not null)
        {
            return cached;
        }

        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);

        try
        {
            var vector = await embeddingClient.EmbedQueryAsync(query, timeoutSource.Token);
            cache.Set(cacheKey, vector, QueryCacheDuration);
            return vector;
        }
        catch (Exception exception) when (
            exception is EmbeddingUnavailableException
            || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested))
        {
            // Lỗi hoặc quá giờ: nghỉ một lúc để không dội thêm request vào nhà cung cấp đang trục trặc.
            cache.Set(CooldownCacheKey, timeProvider.GetUtcNow().Add(FailureCooldown), FailureCooldown);
            logger.LogWarning(
                "Semantic scoring unavailable; skipping embedding calls for {CooldownSeconds} seconds",
                (int)FailureCooldown.TotalSeconds);
            return null;
        }
    }

    private bool IsCoolingDown() =>
        cache.TryGetValue(CooldownCacheKey, out DateTimeOffset until) && until > timeProvider.GetUtcNow();

    private static string Normalize(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static double Dot(float[] left, float[] right)
    {
        var sum = 0d;
        for (var index = 0; index < left.Length; index++)
        {
            sum += (double)left[index] * right[index];
        }

        return sum;
    }
}
