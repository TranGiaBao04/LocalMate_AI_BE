using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalMateAI.Application.Interfaces.Services;
using LocalMateAI.Infrastructure.Llm;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Tests;

public sealed class GeminiLlmClientTests
{
    private const string ApiKey = "test-key-not-real";
    private const string Answer = "{\"stops\":[{\"placeId\":\"a1\",\"reason\":\"Quán yên tĩnh\"}]}";

    [Fact]
    public async Task GenerateJsonAsync_SendsModelInstructionSchemaAndGenerationConfig()
    {
        var handler = new StubHandler(_ => Completed(Answer));
        var schema = JsonNode.Parse("{\"type\":\"object\",\"required\":[\"stops\"]}")!;

        await CreateClient(handler).GenerateJsonAsync(
            new LlmJsonRequest("Chỉ dùng dữ liệu được cung cấp.", "{\"note\":\"yên tĩnh\"}", schema, 800, 0.2));

        var request = Assert.Single(handler.Requests);
        Assert.Equal("https://generativelanguage.googleapis.com/v1beta/interactions", request.Url);
        Assert.Equal(ApiKey, request.ApiKeyHeader);

        var body = request.Body.RootElement;
        Assert.Equal("gemini-3.5-flash-lite", body.GetProperty("model").GetString());
        Assert.Equal("Chỉ dùng dữ liệu được cung cấp.", body.GetProperty("system_instruction").GetString());
        Assert.Equal("{\"note\":\"yên tĩnh\"}", body.GetProperty("input").GetString());
        Assert.False(body.GetProperty("store").GetBoolean());

        var generation = body.GetProperty("generation_config");
        Assert.Equal(0.2, generation.GetProperty("temperature").GetDouble());
        Assert.Equal(800, generation.GetProperty("max_output_tokens").GetInt32());
        Assert.Equal("minimal", generation.GetProperty("thinking_level").GetString());

        var format = body.GetProperty("response_format");
        Assert.Equal("text", format.GetProperty("type").GetString());
        Assert.Equal("application/json", format.GetProperty("mime_type").GetString());
        Assert.Equal("stops", format.GetProperty("schema").GetProperty("required")[0].GetString());
    }

    [Fact]
    public async Task GenerateJsonAsync_ReadsModelOutputAndTokens_SkippingThoughtSteps()
    {
        var handler = new StubHandler(_ => Json("""
            {
              "status": "completed",
              "usage": { "total_input_tokens": 1103, "total_output_tokens": 427 },
              "steps": [
                { "type": "thought", "signature": "abc" },
                { "type": "model_output", "content": [
                    { "type": "text", "text": "{\"stops\":" },
                    { "type": "image", "data": "ignored" },
                    { "type": "text", "text": "[]}" } ] }
              ]
            }
            """));

        var response = await CreateClient(handler).GenerateJsonAsync(Request());

        Assert.Equal("{\"stops\":[]}", response.Json);
        Assert.Equal(1103, response.InputTokens);
        Assert.Equal(427, response.OutputTokens);
    }

    [Fact]
    public async Task GenerateJsonAsync_MissingUsage_ReportsZeroTokens()
    {
        var handler = new StubHandler(_ => Json(
            "{\"status\":\"completed\",\"steps\":[{\"type\":\"model_output\",\"content\":[{\"type\":\"text\",\"text\":\"{}\"}]}]}"));

        var response = await CreateClient(handler).GenerateJsonAsync(Request());

        Assert.Equal(0, response.InputTokens);
        Assert.Equal(0, response.OutputTokens);
    }

    [Fact]
    public async Task GenerateJsonAsync_DoesNotModifyTheCallersSchema()
    {
        var schema = JsonNode.Parse("{\"type\":\"object\"}")!;
        var client = CreateClient(new StubHandler(_ => Completed(Answer)));

        await client.GenerateJsonAsync(new LlmJsonRequest("s", "i", schema, 100));
        await client.GenerateJsonAsync(new LlmJsonRequest("s", "i", schema, 100));

        Assert.Null(schema.Parent);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.ServiceUnavailable)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task GenerateJsonAsync_NonSuccessStatus_ThrowsUnavailable(HttpStatusCode statusCode)
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent("{\"error\":{\"message\":\"high demand\"}}", Encoding.UTF8, "application/json")
        });

        await Assert.ThrowsAsync<LlmUnavailableException>(() => CreateClient(handler).GenerateJsonAsync(Request()));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"status\":\"completed\",\"steps\":[]}")]
    [InlineData("{\"status\":\"completed\",\"steps\":[{\"type\":\"thought\"}]}")]
    [InlineData("{\"status\":\"completed\",\"steps\":[{\"type\":\"model_output\",\"content\":[{\"type\":\"text\",\"text\":\"  \"}]}]}")]
    [InlineData("{\"status\":\"incomplete\",\"steps\":[{\"type\":\"model_output\",\"content\":[{\"type\":\"text\",\"text\":\"{}\"}]}]}")]
    [InlineData("{\"status\":\"failed\"}")]
    public async Task GenerateJsonAsync_IncompleteOrMalformedResponse_ThrowsUnavailable(string body)
    {
        var handler = new StubHandler(_ => Json(body));

        await Assert.ThrowsAsync<LlmUnavailableException>(() => CreateClient(handler).GenerateJsonAsync(Request()));
    }

    [Fact]
    public async Task GenerateJsonAsync_NetworkFailureOrProviderTimeout_ThrowsUnavailable()
    {
        var network = new StubHandler(_ => throw new HttpRequestException("connection refused"));
        var timeout = new StubHandler(_ => throw new TaskCanceledException("timed out"));

        await Assert.ThrowsAsync<LlmUnavailableException>(() => CreateClient(network).GenerateJsonAsync(Request()));
        await Assert.ThrowsAsync<LlmUnavailableException>(() => CreateClient(timeout).GenerateJsonAsync(Request()));
    }

    [Fact]
    public async Task GenerateJsonAsync_CallerCancels_PropagatesCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        var handler = new StubHandler(_ =>
        {
            cancellation.Cancel();
            throw new TaskCanceledException("cancelled by caller");
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => CreateClient(handler).GenerateJsonAsync(Request(), cancellation.Token));
    }

    [Theory]
    [InlineData("", "gemini-3.5-flash-lite")]
    [InlineData("   ", "gemini-3.5-flash-lite")]
    [InlineData(ApiKey, "")]
    public async Task NotConfigured_DoesNotCallProvider(string apiKey, string model)
    {
        var handler = new StubHandler(_ => Completed(Answer));
        var client = CreateClient(handler, new LlmOptions { ApiKey = apiKey, Model = model });

        Assert.False(client.IsConfigured);
        await Assert.ThrowsAsync<LlmUnavailableException>(() => client.GenerateJsonAsync(Request()));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public void ConfiguredClient_ExposesModelName()
    {
        var client = CreateClient(new StubHandler(_ => Completed(Answer)));

        Assert.True(client.IsConfigured);
        Assert.Equal("gemini-3.5-flash-lite", client.Model);
    }

    [Fact]
    public async Task ProviderError_LogDoesNotContainKeyOrUserText()
    {
        var logger = new RecordingLogger();
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent("ghi chú bí mật của user", Encoding.UTF8, "text/plain")
        });

        await Assert.ThrowsAsync<LlmUnavailableException>(() => CreateClient(handler, logger: logger)
            .GenerateJsonAsync(new LlmJsonRequest("s", "ghi chú bí mật của user", JsonNode.Parse("{}")!, 100)));

        var message = Assert.Single(logger.Messages);
        Assert.Contains("503", message);
        Assert.DoesNotContain(ApiKey, message);
        Assert.DoesNotContain("bí mật", message);
    }

    private static LlmJsonRequest Request() =>
        new("Chỉ dùng dữ liệu được cung cấp.", "{}", JsonNode.Parse("{\"type\":\"object\"}")!, 500);

    private static GeminiLlmClient CreateClient(
        StubHandler handler,
        LlmOptions? options = null,
        RecordingLogger? logger = null) =>
        new(
            new HttpClient(handler),
            Options.Create(options ?? new LlmOptions { ApiKey = ApiKey }),
            logger ?? new RecordingLogger());

    private static HttpResponseMessage Completed(string text) =>
        Json(JsonSerializer.Serialize(new
        {
            status = "completed",
            usage = new { total_input_tokens = 10, total_output_tokens = 20 },
            steps = new object[]
            {
                new { type = "model_output", content = new object[] { new { type = "text", text } } }
            }
        }));

    private static HttpResponseMessage Json(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private sealed record CapturedRequest(string Url, string? ApiKeyHeader, JsonDocument Body);

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

    private sealed class RecordingLogger : ILogger<GeminiLlmClient>
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
