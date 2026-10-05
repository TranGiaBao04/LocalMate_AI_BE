using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Infrastructure.Embeddings;

public sealed class GeminiEmbeddingClient(
    HttpClient httpClient,
    IOptions<EmbeddingOptions> embeddingOptions,
    ILogger<GeminiEmbeddingClient> logger) : IEmbeddingClient
{
    public const int MaxBatchSize = 50;

    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models/";
    private const string ApiKeyHeader = "x-goog-api-key";

    private readonly EmbeddingOptions options = embeddingOptions.Value;

    // Kèm số chiều: đổi Dimensions cũng làm vector cũ hết dùng được, job phải tính lại.
    public string Model => $"{options.Model}@{options.Dimensions}";
    public bool IsConfigured => options.IsUsable;

    public async Task<IReadOnlyList<float[]>> EmbedDocumentsAsync(
        IReadOnlyList<EmbeddingDocument> documents,
        CancellationToken cancellationToken = default)
    {
        var vectors = new List<float[]>(documents.Count);

        foreach (var batch in documents.Chunk(MaxBatchSize))
        {
            // gemini-embedding-2 không có tham số task_type: loại tác vụ khai báo ngay trong văn bản.
            var texts = batch.Select(document => $"title: {document.Title} | text: {document.Text}").ToArray();
            vectors.AddRange(await SendAsync(texts, cancellationToken));
        }

        return vectors;
    }

    public async Task<float[]> EmbedQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        var vectors = await SendAsync([$"task: search result | query: {query}"], cancellationToken);
        return vectors[0];
    }

    private async Task<float[][]> SendAsync(string[] texts, CancellationToken cancellationToken)
    {
        if (!IsConfigured)
        {
            throw new EmbeddingUnavailableException("Embedding is not configured.");
        }

        var modelPath = $"models/{options.Model}";
        var payload = new BatchRequest(texts
            .Select(text => new EmbedRequest(modelPath, new Content([new Part(text)]), options.Dimensions))
            .ToArray());

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{BaseUrl}{Uri.EscapeDataString(options.Model)}:batchEmbedContents");
        request.Headers.Add(ApiKeyHeader, options.ApiKey);
        request.Content = JsonContent.Create(payload);

        try
        {
            using var response = await httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Không ghi nội dung gửi đi hay nội dung trả về vào log: có thể chứa câu của user.
                logger.LogWarning("Embedding provider returned HTTP {StatusCode}.", (int)response.StatusCode);
                throw new EmbeddingUnavailableException(
                    $"Embedding provider returned HTTP {(int)response.StatusCode}.");
            }

            var body = await response.Content.ReadFromJsonAsync<BatchResponse>(cancellationToken);
            var vectors = body?.Embeddings?.Select(embedding => embedding.Values).ToArray();

            if (vectors is null
                || vectors.Length != texts.Length
                || vectors.Any(vector => vector is null || vector.Length != options.Dimensions))
            {
                throw new EmbeddingUnavailableException("Embedding provider returned an unexpected payload.");
            }

            return vectors.Select(vector => Normalize(vector!)).ToArray();
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new EmbeddingUnavailableException("Embedding provider call failed.", exception);
        }
    }

    private static float[] Normalize(float[] vector)
    {
        var length = Math.Sqrt(vector.Sum(value => (double)value * value));
        return length == 0 ? vector : vector.Select(value => (float)(value / length)).ToArray();
    }

    private sealed record BatchRequest([property: JsonPropertyName("requests")] EmbedRequest[] Requests);

    private sealed record EmbedRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("content")] Content Content,
        [property: JsonPropertyName("output_dimensionality")] int OutputDimensionality);

    private sealed record Content([property: JsonPropertyName("parts")] Part[] Parts);

    private sealed record Part([property: JsonPropertyName("text")] string Text);

    private sealed record BatchResponse([property: JsonPropertyName("embeddings")] Embedding[]? Embeddings);

    private sealed record Embedding([property: JsonPropertyName("values")] float[]? Values);
}
