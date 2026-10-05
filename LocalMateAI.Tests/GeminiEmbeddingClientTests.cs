using System.Net;
using System.Text;
using System.Text.Json;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Infrastructure.Embeddings;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Tests;

public sealed class GeminiEmbeddingClientTests
{
    private const string ApiKey = "test-key-not-real";
    private const int Dimensions = 128;

    [Fact]
    public async Task EmbedDocumentsAsync_SendsModelKeyDimensionsAndDocumentFormat()
    {
        var handler = new StubHandler(request => Json(OneHotVectors(request.TextCount)));
        var client = CreateClient(handler);

        await client.EmbedDocumentsAsync([new EmbeddingDocument("Tonkin Coffee", "Loại: Quán cà phê. Yên tĩnh")]);

        var request = Assert.Single(handler.Requests);
        Assert.Equal(
            "https://generativelanguage.googleapis.com/v1beta/models/gemini-embedding-2:batchEmbedContents",
            request.Url);
        Assert.Equal(ApiKey, request.ApiKeyHeader);

        var item = Assert.Single(request.Body.RootElement.GetProperty("requests").EnumerateArray());
        Assert.Equal("models/gemini-embedding-2", item.GetProperty("model").GetString());
        Assert.Equal(Dimensions, item.GetProperty("output_dimensionality").GetInt32());
        Assert.Equal(
            "title: Tonkin Coffee | text: Loại: Quán cà phê. Yên tĩnh",
            item.GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task EmbedQueryAsync_SendsSearchTaskPrefix()
    {
        var handler = new StubHandler(request => Json(OneHotVectors(request.TextCount)));

        var vector = await CreateClient(handler).EmbedQueryAsync("quán yên tĩnh");

        Assert.Equal(Dimensions, vector.Length);
        Assert.Equal(["task: search result | query: quán yên tĩnh"], Assert.Single(handler.Requests).Texts);
    }

    [Fact]
    public async Task EmbedDocumentsAsync_SplitsIntoBatchesAndKeepsOrder()
    {
        var sent = 0;
        var handler = new StubHandler(request =>
        {
            var vectors = Enumerable.Range(sent, request.TextCount).Select(OneHot).ToArray();
            sent += request.TextCount;
            return Json(vectors);
        });
        var documents = Enumerable.Range(0, 120)
            .Select(index => new EmbeddingDocument($"Place {index}", "text"))
            .ToArray();

        var vectors = await CreateClient(handler).EmbedDocumentsAsync(documents);

        Assert.Equal([50, 50, 20], handler.Requests.Select(request => request.TextCount));
        Assert.Equal(120, vectors.Count);
        Assert.All(Enumerable.Range(0, 120), index => Assert.Equal(1f, vectors[index][index]));
        Assert.Equal("title: Place 119 | text: text", handler.Requests[2].Texts[^1]);
    }

    [Fact]
    public async Task EmbedQueryAsync_NormalizesVectorToUnitLength()
    {
        var raw = new float[Dimensions];
        raw[0] = 3f;
        raw[1] = 4f;
        var handler = new StubHandler(_ => Json([raw]));

        var vector = await CreateClient(handler).EmbedQueryAsync("câu hỏi");

        Assert.Equal(0.6f, vector[0], 5);
        Assert.Equal(0.8f, vector[1], 5);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task EmbedQueryAsync_NonSuccessStatus_ThrowsUnavailable(HttpStatusCode statusCode)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("{\"error\":{\"message\":\"nope\"}}", Encoding.UTF8, "application/json")
        });

        await Assert.ThrowsAsync<EmbeddingUnavailableException>(() => CreateClient(handler).EmbedQueryAsync("câu hỏi"));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("{}")]
    [InlineData("{\"embeddings\":[]}")]
    [InlineData("{\"embeddings\":[{}]}")]
    [InlineData("{\"embeddings\":[{\"values\":[0.1,0.2]}]}")]
    public async Task EmbedQueryAsync_UnexpectedPayload_ThrowsUnavailable(string body)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });

        await Assert.ThrowsAsync<EmbeddingUnavailableException>(() => CreateClient(handler).EmbedQueryAsync("câu hỏi"));
    }

    [Fact]
    public async Task EmbedQueryAsync_NonJsonContentType_ThrowsUnavailable()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<html>gateway</html>", Encoding.UTF8, "text/html")
        });

        await Assert.ThrowsAsync<EmbeddingUnavailableException>(() => CreateClient(handler).EmbedQueryAsync("câu hỏi"));
    }

    [Fact]
    public async Task EmbedDocumentsAsync_FewerVectorsThanTexts_ThrowsUnavailable()
    {
        var handler = new StubHandler(_ => Json(OneHotVectors(1)));
        EmbeddingDocument[] documents = [new("A", "a"), new("B", "b")];

        await Assert.ThrowsAsync<EmbeddingUnavailableException>(
            () => CreateClient(handler).EmbedDocumentsAsync(documents));
    }

    [Fact]
    public async Task EmbedQueryAsync_NetworkFailure_ThrowsUnavailable()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("connection refused"));

        await Assert.ThrowsAsync<EmbeddingUnavailableException>(() => CreateClient(handler).EmbedQueryAsync("câu hỏi"));
    }

    [Fact]
    public async Task EmbedQueryAsync_ProviderTimeout_ThrowsUnavailable()
    {
        var handler = new StubHandler(_ => throw new TaskCanceledException("timed out"));

        await Assert.ThrowsAsync<EmbeddingUnavailableException>(() => CreateClient(handler).EmbedQueryAsync("câu hỏi"));
    }

    [Fact]
    public async Task EmbedQueryAsync_CallerCancels_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new StubHandler(_ =>
        {
            cancellation.Cancel();
            throw new TaskCanceledException("cancelled by caller");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateClient(handler).EmbedQueryAsync("câu hỏi", cancellation.Token));
    }

    [Theory]
    [InlineData("", "gemini-embedding-2", 768)]
    [InlineData("   ", "gemini-embedding-2", 768)]
    [InlineData(ApiKey, "", 768)]
    [InlineData(ApiKey, "gemini-embedding-2", 127)]
    [InlineData(ApiKey, "gemini-embedding-2", 3073)]
    public async Task NotConfigured_DoesNotCallProvider(string apiKey, string model, int dimensions)
    {
        var handler = new StubHandler(request => Json(OneHotVectors(request.TextCount)));
        var client = CreateClient(handler, new EmbeddingOptions { ApiKey = apiKey, Model = model, Dimensions = dimensions });

        Assert.False(client.IsConfigured);
        await Assert.ThrowsAsync<EmbeddingUnavailableException>(() => client.EmbedQueryAsync("câu hỏi"));
        await Assert.ThrowsAsync<EmbeddingUnavailableException>(
            () => client.EmbedDocumentsAsync([new EmbeddingDocument("A", "a")]));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void ConfiguredClient_ExposesModelName()
    {
        var client = CreateClient(new StubHandler(_ => Json([])));

        Assert.True(client.IsConfigured);
        Assert.Equal("gemini-embedding-2@128", client.Model);
    }

    [Fact]
    public async Task ProviderError_LogDoesNotContainKeyOrText()
    {
        var logger = new RecordingLogger();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("ghi chú bí mật của user", Encoding.UTF8, "text/plain")
        });

        await Assert.ThrowsAsync<EmbeddingUnavailableException>(
            () => CreateClient(handler, logger: logger).EmbedQueryAsync("ghi chú bí mật của user"));

        var message = Assert.Single(logger.Messages);
        Assert.Contains("500", message);
        Assert.DoesNotContain(ApiKey, message);
        Assert.DoesNotContain("bí mật", message);
    }

    private static GeminiEmbeddingClient CreateClient(
        StubHandler handler,
        EmbeddingOptions? options = null,
        RecordingLogger? logger = null) =>
        new(
            new HttpClient(handler),
            Options.Create(options ?? new EmbeddingOptions { ApiKey = ApiKey, Dimensions = Dimensions }),
            logger ?? new RecordingLogger());

    private static float[] OneHot(int index)
    {
        var vector = new float[Dimensions];
        vector[index % Dimensions] = 1f;
        return vector;
    }

    private static float[][] OneHotVectors(int count) => Enumerable.Range(0, count).Select(OneHot).ToArray();

    private static HttpResponseMessage Json(float[][] vectors) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new { embeddings = vectors.Select(values => new { values }) }),
                Encoding.UTF8,
                "application/json")
        };

    private sealed record CapturedRequest(string Url, string? ApiKeyHeader, JsonDocument Body)
    {
        public string[] Texts => Body.RootElement.GetProperty("requests").EnumerateArray()
            .Select(item => item.GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString()!)
            .ToArray();

        public int TextCount => Body.RootElement.GetProperty("requests").GetArrayLength();
    }

    private sealed class StubHandler(Func<CapturedRequest, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var captured = new CapturedRequest(
                request.RequestUri!.ToString(),
                request.Headers.TryGetValues("x-goog-api-key", out var values) ? values.Single() : null,
                JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken)));
            Requests.Add(captured);

            return respond(captured);
        }
    }

    private sealed class RecordingLogger : ILogger<GeminiEmbeddingClient>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) =>
            Messages.Add(formatter(state, exception));
    }
}
