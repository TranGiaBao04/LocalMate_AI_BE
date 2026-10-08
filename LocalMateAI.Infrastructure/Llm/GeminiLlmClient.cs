using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using LocalMateAI.Application.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LocalMateAI.Infrastructure.Llm;

public sealed class GeminiLlmClient(
    HttpClient httpClient,
    IOptions<LlmOptions> llmOptions,
    ILogger<GeminiLlmClient> logger) : ILlmClient
{
    private const string Endpoint = "https://generativelanguage.googleapis.com/v1beta/interactions";
    private const string ApiKeyHeader = "x-goog-api-key";
    private const string CompletedStatus = "completed";

    private readonly LlmOptions options = llmOptions.Value;

    public string Model => options.Model;
    public bool IsConfigured => options.IsUsable;

    public async Task<LlmJsonResponse> GenerateJsonAsync(
        LlmJsonRequest request,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            throw new LlmUnavailableException("LLM is not configured.");
        }

        var payload = new JsonObject
        {
            ["model"] = options.Model,
            ["input"] = request.Input,
            ["system_instruction"] = request.SystemInstruction,
            // Không lưu hội thoại phía nhà cung cấp: mỗi lần gọi là độc lập.
            ["store"] = false,
            ["generation_config"] = new JsonObject
            {
                ["temperature"] = request.Temperature,
                ["max_output_tokens"] = request.MaxOutputTokens,
                ["thinking_level"] = options.ThinkingLevel
            },
            ["response_format"] = new JsonObject
            {
                ["type"] = "text",
                ["mime_type"] = "application/json",
                ["schema"] = request.Schema.DeepClone()
            }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        message.Headers.Add(ApiKeyHeader, options.ApiKey);
        message.Content = new StringContent(payload.ToJsonString(), Encoding.UTF8, "application/json");

        try
        {
            using var response = await httpClient.SendAsync(message, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                // Không ghi nội dung gửi đi hay nội dung trả về vào log: có thể chứa câu của user.
                logger.LogWarning("LLM provider returned HTTP {StatusCode}.", (int)response.StatusCode);
                throw new LlmUnavailableException($"LLM provider returned HTTP {(int)response.StatusCode}.");
            }

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            var root = document.RootElement;
            var text = ReadOutputText(root);

            if (ReadString(root, "status") != CompletedStatus || string.IsNullOrWhiteSpace(text))
            {
                throw new LlmUnavailableException("LLM provider returned an incomplete response.");
            }

            return new LlmJsonResponse(
                text,
                ReadUsage(root, "total_input_tokens"),
                ReadUsage(root, "total_output_tokens"));
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new LlmUnavailableException("LLM provider call failed.", exception);
        }
    }

    // Văn bản nằm ở steps[type = "model_output"].content[type = "text"].text (các bước "thought" bị bỏ qua).
    private static string ReadOutputText(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("steps", out var steps)
            || steps.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        foreach (var step in steps.EnumerateArray())
        {
            if (ReadString(step, "type") != "model_output"
                || !step.TryGetProperty("content", out var content)
                || content.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            foreach (var part in content.EnumerateArray())
            {
                if (ReadString(part, "type") == "text")
                {
                    builder.Append(ReadString(part, "text"));
                }
            }
        }

        return builder.ToString();
    }

    private static string? ReadString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static int ReadUsage(JsonElement root, string name) =>
        root.ValueKind == JsonValueKind.Object
        && root.TryGetProperty("usage", out var usage)
        && usage.ValueKind == JsonValueKind.Object
        && usage.TryGetProperty(name, out var value)
        && value.ValueKind == JsonValueKind.Number
        && value.TryGetInt32(out var tokens)
            ? tokens
            : 0;
}
